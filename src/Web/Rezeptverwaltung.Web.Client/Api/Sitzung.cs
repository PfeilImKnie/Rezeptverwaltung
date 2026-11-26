using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;

namespace Rezeptverwaltung.Web.Client.Api;

/// <summary>Wer angemeldet ist und was die Person darf; einmal je Seitenaufruf geladen.</summary>
public sealed class Sitzung(HttpClient http, IRezeptApi api, NavigationManager navigation)
{
    private Task<SitzungsInfo>? _laden;

    public Task<SitzungsInfo> LadenAsync() => _laden ??= LadenIntern();

    private async Task<SitzungsInfo> LadenIntern()
    {
        var benutzer = await BenutzerLadenAsync().ConfigureAwait(false);
        try
        {
            return new SitzungsInfo(benutzer.Name, benutzer.AbmeldeToken, await api.BerechtigungenAsync().ConfigureAwait(false), null);
        }
        catch (ApiFehlerException fehler) when (fehler.Status == 403)
        {
            return new SitzungsInfo(benutzer.Name, benutzer.AbmeldeToken, Berechtigungen.Keine, fehler.Message);
        }
    }

    private async Task<BenutzerInfo> BenutzerLadenAsync()
    {
        HttpResponseMessage antwort;
        try
        {
            antwort = await http.GetAsync(new Uri("bff/benutzer", UriKind.Relative)).ConfigureAwait(false);
        }
        catch (Exception ausnahme) when (ausnahme is HttpRequestException or IOException or TaskCanceledException)
        {
            throw new ApiFehlerException("Keine Verbindung zum Server. Bitte WLAN prüfen und neu laden.", ausnahme);
        }

        using (antwort)
        {
            if (antwort.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Sitzung zwischen Seitenaufruf und Abfrage abgelaufen: neu anmelden, danach hierher zurück.
                var hier = "/" + navigation.ToBaseRelativePath(navigation.Uri);
                navigation.NavigateTo($"anmelden?returnUrl={Uri.EscapeDataString(hier)}", forceLoad: true);
                throw new ApiFehlerException(401, null, "Die Anmeldung ist abgelaufen. Du wirst neu angemeldet …");
            }

            if (!antwort.IsSuccessStatusCode)
            {
                throw await RezeptApi.FehlerAusAsync(antwort, CancellationToken.None).ConfigureAwait(false);
            }

            return await antwort.Content.ReadFromJsonAsync<BenutzerInfo>(RezeptApi.Json).ConfigureAwait(false)
                ?? throw new ApiFehlerException(500, null, "Die Anmeldung konnte nicht gelesen werden.");
        }
    }

    private sealed record BenutzerInfo(string Name, string AbmeldeToken);
}

/// <param name="Name">Anzeigename der angemeldeten Person.</param>
/// <param name="AbmeldeToken">Antiforgery-Token für das Abmelde-Formular.</param>
/// <param name="Rechte">Was die Person mit Rezepten darf.</param>
/// <param name="Hinweis">Erklärung, warum die Person nichts darf (z. B. Rollen in mehreren Betrieben).</param>
public sealed record SitzungsInfo(string Name, string AbmeldeToken, Berechtigungen Rechte, string? Hinweis);
