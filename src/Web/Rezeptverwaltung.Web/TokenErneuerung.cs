using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Rezeptverwaltung.Web;

/// <summary>
/// Erneuert den Access Token der Sitzung kurz vor Ablauf mit dem Refresh Token (ADR 0016). Gelingt das nicht (abgelehnt,
/// Identity Provider nicht erreichbar), gilt der alte Token bis zu seinem Ablauf weiter und der nächste Aufruf versucht
/// es neu. Ist er abgelaufen, endet die Sitzung: Seiten leiten dann zum Login, API-Aufrufe bekommen 401.
/// </summary>
/// <remarks>
/// Kurz vor Ablauf kommen oft mehrere Anfragen mit demselben Cookie gleichzeitig (z. B. Suche beim Tippen). Sie teilen
/// sich <b>eine</b> Erneuerung je Refresh Token. Zitadel rotiert Refresh Tokens; eine zweite Erneuerung mit dem schon
/// verbrauchten Token würde abgelehnt und die Person abmelden.
/// </remarks>
internal sealed class TokenErneuerung(IOptionsMonitor<OpenIdConnectOptions> oidcOptionen, TimeProvider zeit)
{
    /// <summary>So lange vor Ablauf wird erneuert, damit ein Token nicht unterwegs zur API abläuft.</summary>
    public static readonly TimeSpan Vorlauf = TimeSpan.FromMinutes(2);

    /// <summary>So lange bleibt das Ergebnis einer Erneuerung für weitere Anfragen mit demselben alten Cookie gültig.</summary>
    public static readonly TimeSpan Teilen = TimeSpan.FromMinutes(1);

    /// <summary>Höchstens so lange wartet eine Anfrage auf den Identity Provider; danach gilt die Erneuerung als gescheitert.</summary>
    public static readonly TimeSpan Zeitgrenze = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, Erneuerung> _erneuerungen = new(StringComparer.Ordinal);

    public async Task PruefenAsync(CookieValidatePrincipalContext kontext)
    {
        ArgumentNullException.ThrowIfNull(kontext);
        var ablauf = kontext.Properties.GetTokenValue("expires_at");
        if (ablauf is null
            || !DateTimeOffset.TryParse(ablauf, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var ablaufZeit))
        {
            kontext.RejectPrincipal();
            return;
        }

        var jetzt = zeit.GetUtcNow();
        if (jetzt + Vorlauf < ablaufZeit)
        {
            return;
        }

        var refreshToken = kontext.Properties.GetTokenValue("refresh_token");
        var eintrag = string.IsNullOrEmpty(refreshToken) ? null : GemeinsamErneuern(refreshToken, jetzt);
        var neu = eintrag is null ? null : await eintrag.Ergebnis.Value.ConfigureAwait(false);
        if (neu is null)
        {
            if (eintrag is not null)
            {
                // Nur den eigenen, gescheiterten Eintrag entfernen; der nächste Aufruf versucht es neu.
                _erneuerungen.TryRemove(new KeyValuePair<string, Erneuerung>(refreshToken!, eintrag));
            }

            if (jetzt < ablaufZeit)
            {
                return; // alter Token gilt noch: kurzer Ausfall des Identity Providers beendet keine Sitzung
            }

            kontext.RejectPrincipal();
            await kontext.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
            return;
        }

        kontext.Properties.StoreTokens(
        [
            new() { Name = "access_token", Value = neu.AccessToken },
            new() { Name = "refresh_token", Value = neu.RefreshToken ?? refreshToken! },
            new() { Name = "id_token", Value = neu.IdToken ?? kontext.Properties.GetTokenValue("id_token")! },
            new() { Name = "token_type", Value = neu.TokenType ?? "Bearer" },
            new() { Name = "expires_at", Value = neu.Ablauf.ToString("o", CultureInfo.InvariantCulture) },
        ]);
        kontext.ShouldRenew = true;
    }

    private Erneuerung GemeinsamErneuern(string refreshToken, DateTimeOffset jetzt)
    {
        foreach (var (schluessel, alt) in _erneuerungen)
        {
            if (jetzt - alt.Seit > Teilen)
            {
                _erneuerungen.TryRemove(schluessel, out _);
            }
        }

        // Ohne Abbruch durch die einzelne Anfrage: Andere Anfragen warten auf dasselbe Ergebnis.
        return _erneuerungen.GetOrAdd(refreshToken, token => new Erneuerung(jetzt, new Lazy<Task<NeueTokens?>>(() => ErneuernAsync(token, jetzt))));
    }

    private async Task<NeueTokens?> ErneuernAsync(string refreshToken, DateTimeOffset jetzt)
    {
        using var zeitgrenze = new CancellationTokenSource(Zeitgrenze);
        try
        {
            return await AnfragenAsync(refreshToken, jetzt, zeitgrenze.Token).ConfigureAwait(false);
        }
        catch (Exception ausnahme) when (ausnahme is HttpRequestException or IOException or OperationCanceledException
            or InvalidOperationException or ArgumentException)
        {
            // Nicht erreichbar, zu langsam, Metadaten nicht ladbar oder unlesbare Antwort (z. B. HTML-Fehlerseite eines
            // Proxys): wie eine abgelehnte Erneuerung behandeln.
            return null;
        }
    }

    private async Task<NeueTokens?> AnfragenAsync(string refreshToken, DateTimeOffset jetzt, CancellationToken cancellationToken)
    {
        var optionen = oidcOptionen.Get(OpenIdConnectDefaults.AuthenticationScheme);
        var konfiguration = await optionen.ConfigurationManager!.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

        using var anfrage = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = optionen.ClientId!,
            ["client_secret"] = optionen.ClientSecret!,
            ["refresh_token"] = refreshToken,
        });
        using var antwort = await optionen.Backchannel.PostAsync(new Uri(konfiguration.TokenEndpoint), anfrage, cancellationToken).ConfigureAwait(false);
        if (!antwort.IsSuccessStatusCode)
        {
            return null;
        }

        var nachricht = new OpenIdConnectMessage(await antwort.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (string.IsNullOrEmpty(nachricht.AccessToken)
            || !int.TryParse(nachricht.ExpiresIn, CultureInfo.InvariantCulture, out var gueltigSekunden))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(nachricht.IdToken))
        {
            // Ein neuer ID-Token wird wie beim Login geprüft (Signatur, Aussteller, Zielgruppe), bevor er gespeichert wird.
            var parameter = optionen.TokenValidationParameters.Clone();
            parameter.ValidIssuer = konfiguration.Issuer;
            parameter.ValidAudience = optionen.ClientId;
            parameter.IssuerSigningKeys = konfiguration.SigningKeys;
            var pruefung = await optionen.TokenHandler.ValidateTokenAsync(nachricht.IdToken, parameter).ConfigureAwait(false);
            if (!pruefung.IsValid)
            {
                return null;
            }
        }

        return new NeueTokens(
            nachricht.AccessToken,
            string.IsNullOrEmpty(nachricht.RefreshToken) ? null : nachricht.RefreshToken,
            string.IsNullOrEmpty(nachricht.IdToken) ? null : nachricht.IdToken,
            nachricht.TokenType,
            jetzt + TimeSpan.FromSeconds(gueltigSekunden));
    }

    private sealed record Erneuerung(DateTimeOffset Seit, Lazy<Task<NeueTokens?>> Ergebnis);

    private sealed record NeueTokens(string AccessToken, string? RefreshToken, string? IdToken, string? TokenType, DateTimeOffset Ablauf);
}
