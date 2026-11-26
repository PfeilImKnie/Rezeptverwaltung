using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;

namespace Rezeptverwaltung.Web.Client.Api;

/// <summary>HTTP-Zugriff auf <c>/api/v1/rezepte</c>. Ist die Sitzung abgelaufen (401), geht es zum Login.</summary>
public sealed class RezeptApi(HttpClient http, NavigationManager navigation) : IRezeptApi
{
    private const string Basis = "api/v1/rezepte";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IReadOnlyList<RezeptUebersicht>> AuflistenAsync(string? suche, CancellationToken cancellationToken = default)
    {
        var adresse = string.IsNullOrWhiteSpace(suche) ? Basis : $"{Basis}?suche={Uri.EscapeDataString(suche)}";
        using var antwort = await SendenAsync(HttpMethod.Get, adresse, null, null, cancellationToken).ConfigureAwait(false);
        return (await LesenAsync<List<RezeptUebersicht>>(antwort, cancellationToken).ConfigureAwait(false)).Wert;
    }

    public async Task<Berechtigungen> BerechtigungenAsync(CancellationToken cancellationToken = default)
    {
        using var antwort = await SendenAsync(HttpMethod.Get, $"{Basis}/berechtigungen", null, null, cancellationToken).ConfigureAwait(false);
        return (await LesenAsync<Berechtigungen>(antwort, cancellationToken).ConfigureAwait(false)).Wert;
    }

    public async Task<MitStand<RezeptDetails>?> RezeptAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var antwort = await SendenAsync(HttpMethod.Get, $"{Basis}/{id}", null, null, cancellationToken, nichtGefundenErlaubt: true)
            .ConfigureAwait(false);
        return antwort.StatusCode == HttpStatusCode.NotFound ? null : await LesenAsync<RezeptDetails>(antwort, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MitStand<FassungDetails>?> FassungAsync(Guid id, int nummer, CancellationToken cancellationToken = default)
    {
        using var antwort = await SendenAsync(HttpMethod.Get, $"{Basis}/{id}/fassungen/{nummer}", null, null, cancellationToken, nichtGefundenErlaubt: true)
            .ConfigureAwait(false);
        return antwort.StatusCode == HttpStatusCode.NotFound ? null : await LesenAsync<FassungDetails>(antwort, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MitStand<RezeptDetails>> AnlegenAsync(string name, RezeptInhalt inhalt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inhalt);
        var koerper = new { name, zutaten = inhalt.Zutaten, bezugsform = inhalt.Bezugsform };
        using var antwort = await SendenAsync(HttpMethod.Post, Basis, koerper, null, cancellationToken).ConfigureAwait(false);
        return await LesenAsync<RezeptDetails>(antwort, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SkaliertesRezept> UmrechnenAsync(Guid id, Umrechnung ziel, int? fassung, CancellationToken cancellationToken = default)
    {
        var adresse = ziel switch
        {
            Umrechnung.AufGewicht g => $"{Basis}/{id}/skaliert?zielgewichtGramm={Zahl(g.Gramm)}",
            Umrechnung.AufForm { Form.Art: Form.Ring } f =>
                $"{Basis}/{id}/skaliert/ring?durchmesserCm={Zahl(f.Form.DurchmesserCm!.Value)}&hoeheCm={Zahl(f.Form.HoeheCm)}",
            Umrechnung.AufForm f =>
                $"{Basis}/{id}/skaliert/blech?laengeCm={Zahl(f.Form.LaengeCm!.Value)}&breiteCm={Zahl(f.Form.BreiteCm!.Value)}&hoeheCm={Zahl(f.Form.HoeheCm)}",
            _ => throw new ArgumentOutOfRangeException(nameof(ziel)),
        };
        if (fassung is { } nummer)
        {
            adresse += $"&fassung={nummer}";
        }

        using var antwort = await SendenAsync(HttpMethod.Get, adresse, null, null, cancellationToken).ConfigureAwait(false);
        return (await LesenAsync<SkaliertesRezept>(antwort, cancellationToken).ConfigureAwait(false)).Wert;
    }

    public async Task<MitStand<FassungDetails>> EntwurfAnlegenAsync(Guid id, string stand, CancellationToken cancellationToken = default)
    {
        using var antwort = await SendenAsync(HttpMethod.Post, $"{Basis}/{id}/entwurf", null, stand, cancellationToken).ConfigureAwait(false);
        return await LesenAsync<FassungDetails>(antwort, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MitStand<FassungDetails>> EntwurfSpeichernAsync(Guid id, string stand, RezeptInhalt inhalt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inhalt);
        var koerper = new { zutaten = inhalt.Zutaten, bezugsform = inhalt.Bezugsform };
        using var antwort = await SendenAsync(HttpMethod.Put, $"{Basis}/{id}/entwurf", koerper, stand, cancellationToken).ConfigureAwait(false);
        return await LesenAsync<FassungDetails>(antwort, cancellationToken).ConfigureAwait(false);
    }

    public async Task EntwurfVerwerfenAsync(Guid id, string stand, CancellationToken cancellationToken = default)
    {
        using var _ = await SendenAsync(HttpMethod.Delete, $"{Basis}/{id}/entwurf", null, stand, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MitStand<FassungDetails>> FreigebenAsync(Guid id, string stand, CancellationToken cancellationToken = default)
    {
        using var antwort = await SendenAsync(HttpMethod.Post, $"{Basis}/{id}/entwurf/freigabe", null, stand, cancellationToken).ConfigureAwait(false);
        return await LesenAsync<FassungDetails>(antwort, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Zahlen in der Adresse immer mit Punkt (Vertrag), unabhängig von der Sprache des Browsers.</summary>
    private static string Zahl(decimal wert) => wert.ToString(CultureInfo.InvariantCulture);

    private async Task<HttpResponseMessage> SendenAsync(
        HttpMethod methode,
        string adresse,
        object? koerper,
        string? stand,
        CancellationToken cancellationToken,
        bool nichtGefundenErlaubt = false)
    {
        using var anfrage = new HttpRequestMessage(methode, adresse);
        if (koerper is not null)
        {
            anfrage.Content = JsonContent.Create(koerper, options: Json);
        }

        if (stand is not null)
        {
            anfrage.Headers.TryAddWithoutValidation("If-Match", stand);
        }

        HttpResponseMessage antwort;
        try
        {
            antwort = await http.SendAsync(anfrage, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ausnahme) when (IstVerbindungsfehler(ausnahme, cancellationToken))
        {
            // Wackliges WLAN in der Backstube: eine Meldung statt eines Absturzes, Eingaben bleiben stehen.
            throw KeineVerbindung(ausnahme);
        }

        if (antwort.IsSuccessStatusCode || (nichtGefundenErlaubt && antwort.StatusCode == HttpStatusCode.NotFound))
        {
            return antwort;
        }

        using (antwort)
        {
            if (antwort.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Sitzung abgelaufen: neu anmelden und danach hierher zurück.
                var hier = "/" + navigation.ToBaseRelativePath(navigation.Uri);
                navigation.NavigateTo($"anmelden?returnUrl={Uri.EscapeDataString(hier)}", forceLoad: true);
                throw new ApiFehlerException(401, null, "Die Anmeldung ist abgelaufen. Du wirst neu angemeldet …");
            }

            throw await FehlerAusAsync(antwort, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Verbindung abgebrochen oder Zeitüberschreitung (nicht: vom Aufrufer abgebrochen).</summary>
    private static bool IstVerbindungsfehler(Exception ausnahme, CancellationToken cancellationToken) =>
        ausnahme is HttpRequestException or IOException
        || (ausnahme is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static ApiFehlerException KeineVerbindung(Exception ausnahme) =>
        new("Keine Verbindung zum Server. Bitte WLAN prüfen und noch einmal versuchen.", ausnahme);

    private static async Task<MitStand<T>> LesenAsync<T>(HttpResponseMessage antwort, CancellationToken cancellationToken)
    {
        T? gelesen;
        try
        {
            gelesen = await antwort.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ausnahme) when (IstVerbindungsfehler(ausnahme, cancellationToken))
        {
            throw KeineVerbindung(ausnahme);
        }

        var wert = gelesen ?? throw new ApiFehlerException((int)antwort.StatusCode, null, "Die Antwort des Servers war leer.");
        return new MitStand<T>(wert, antwort.Headers.ETag?.ToString() ?? string.Empty);
    }

    internal static async Task<ApiFehlerException> FehlerAusAsync(HttpResponseMessage antwort, CancellationToken cancellationToken)
    {
        var status = (int)antwort.StatusCode;
        string? code = null;
        string? meldung = null;
        try
        {
            var problem = await antwort.Content.ReadFromJsonAsync<JsonElement>(Json, cancellationToken).ConfigureAwait(false);
            code = Text(problem, "code");
            meldung = Text(problem, "detail") ?? Text(problem, "title");
        }
        catch (JsonException)
        {
            // Kein Problem-Details-Körper (z. B. vom Server vor der API): Standardmeldung unten.
        }

        return new ApiFehlerException(status, code, status switch
        {
            403 when code == "betrieb.nicht_eindeutig" => meldung ?? "Die Anmeldung gilt für mehrere Betriebe.",
            403 => "Dafür fehlt dir das Recht.",
            404 => "Das Rezept gibt es nicht (mehr).",
            412 => "Das Rezept wurde inzwischen an anderer Stelle geändert. Bitte neu laden.",
            413 => "Die Eingabe ist zu groß.",
            _ when meldung is not null => meldung,
            _ => $"Das hat nicht geklappt (Fehler {status}). Bitte später noch einmal versuchen.",
        });
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var wert) && wert.ValueKind == JsonValueKind.String
            ? wert.GetString()
            : null;
}
