using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Rezeptverwaltung.Rezeptur.Application.Rezepte;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Http;

// API-Verträge sind bewusst von den Application-Modellen getrennt, damit sich beide unabhängig entwickeln können.

internal sealed record RezeptAnlegenRequest(string Name, IReadOnlyList<ZutatRequest> Zutaten, FormRequest? Bezugsform = null);

internal sealed record EntwurfBearbeitenRequest(IReadOnlyList<ZutatRequest> Zutaten, FormRequest? Bezugsform = null);

/// <summary>Bezugsform im Vertrag: <c>art</c> = <c>ring</c> | <c>blech</c>; Maße, die zur Art nicht passen, werden ignoriert.</summary>
internal sealed record FormRequest(string Art, decimal HoeheCm, decimal? DurchmesserCm = null, decimal? LaengeCm = null, decimal? BreiteCm = null)
{
    /// <summary>Unbekannte Art oder fehlende Pflichtmaße sind Formatfehler (400), Wertebereiche prüft die Domain (422).</summary>
    public Form AlsForm() => Art switch
    {
        "ring" => Form.Ring(Pflicht(DurchmesserCm, "durchmesserCm"), HoeheCm),
        "blech" => Form.Blech(Pflicht(LaengeCm, "laengeCm"), Pflicht(BreiteCm, "breiteCm"), HoeheCm),
        _ => throw new BadHttpRequestException("Unbekannte Formart. Erlaubt: ring, blech.", StatusCodes.Status400BadRequest),
    };

    private static decimal Pflicht(decimal? wert, string feld) =>
        wert ?? throw new BadHttpRequestException($"Für diese Formart fehlt '{feld}'.", StatusCodes.Status400BadRequest);
}

internal sealed record FormResponse(
    string Art,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? DurchmesserCm,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? LaengeCm,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? BreiteCm,
    decimal HoeheCm)
{
    public static FormResponse? Aus(Form? form) => form is null
        ? null
        : new(form.Art == Domain.Rezepte.Formart.Ring ? "ring" : "blech", form.DurchmesserCm, form.LaengeCm, form.BreiteCm, form.HoeheCm);
}

internal sealed record ZutatRequest(string Bezeichnung, decimal Gramm)
{
    public static IReadOnlyList<ZutatEingabe> AlsEingaben(IReadOnlyList<ZutatRequest> zutaten) =>
        zutaten.Select(z => new ZutatEingabe(z.Bezeichnung, z.Gramm)).ToList();
}

internal sealed record ZutatResponse(string Bezeichnung, decimal Gramm)
{
    public static ZutatResponse Aus(ZutatMenge zutat) => new(zutat.Bezeichnung, zutat.Gramm);
}

internal sealed record FassungUebersichtResponse(int Nummer, string Status, DateTimeOffset ErstelltAm, DateTimeOffset? FreigegebenAm)
{
    public static FassungUebersichtResponse Aus(FassungUebersicht fassung) =>
        new(fassung.Nummer, StatusText.Von(fassung.Status), fassung.ErstelltAm, fassung.FreigegebenAm);
}

internal sealed record RezeptUebersichtResponse(Guid Id, string Name, int MassgeblicheFassung, int? AktuelleFassung, int? Entwurf)
{
    public static RezeptUebersichtResponse Aus(RezeptUebersicht rezept) =>
        new(rezept.Id, rezept.Name, rezept.MassgeblicheFassung, rezept.AktuelleFassung, rezept.Entwurf);
}

internal sealed record BerechtigungenResponse(bool Lesen, bool Bearbeiten, bool Freigeben)
{
    public static BerechtigungenResponse Aus(Berechtigungen rechte) => new(rechte.Lesen, rechte.Bearbeiten, rechte.Freigeben);
}

internal sealed record RezeptDetailsResponse(
    Guid Id,
    string Name,
    IReadOnlyList<ZutatResponse> Zutaten,
    decimal GesamtgewichtGramm,
    FormResponse? Bezugsform,
    int MassgeblicheFassung,
    int? AktuelleFassung,
    int? Entwurf,
    IReadOnlyList<FassungUebersichtResponse> Fassungen)
{
    public static RezeptDetailsResponse Aus(RezeptDetails details) => new(
        details.Id,
        details.Name,
        details.Zutaten.Select(ZutatResponse.Aus).ToList(),
        details.GesamtgewichtGramm,
        FormResponse.Aus(details.Bezugsform),
        details.MassgeblicheFassung,
        details.AktuelleFassung,
        details.Entwurf,
        details.Fassungen.Select(FassungUebersichtResponse.Aus).ToList());
}

internal sealed record FassungDetailsResponse(
    Guid RezeptId,
    int Nummer,
    string Status,
    DateTimeOffset ErstelltAm,
    DateTimeOffset? FreigegebenAm,
    FormResponse? Bezugsform,
    IReadOnlyList<ZutatResponse> Zutaten,
    decimal GesamtgewichtGramm)
{
    public static FassungDetailsResponse Aus(FassungDetails fassung) => new(
        fassung.RezeptId,
        fassung.Nummer,
        StatusText.Von(fassung.Status),
        fassung.ErstelltAm,
        fassung.FreigegebenAm,
        FormResponse.Aus(fassung.Bezugsform),
        fassung.Zutaten.Select(ZutatResponse.Aus).ToList(),
        fassung.GesamtgewichtGramm);
}

internal sealed record SkaliertesRezeptResponse(
    Guid RezeptId,
    string Name,
    int Fassung,
    decimal Faktor,
    FormResponse? Zielform,
    decimal ZielgewichtGramm,
    IReadOnlyList<ZutatResponse> Zutaten)
{
    public static SkaliertesRezeptResponse Aus(SkaliertesRezept rezept) => new(
        rezept.RezeptId,
        rezept.Name,
        rezept.Fassung,
        rezept.Faktor,
        FormResponse.Aus(rezept.Zielform),
        rezept.ZielgewichtGramm,
        rezept.Zutaten.Select(ZutatResponse.Aus).ToList());
}

/// <summary>
/// Stand als HTTP-ETag (stark, in Anführungszeichen). Für If-Match gilt der starke Vergleich (RFC 9110):
/// Nur ein einzelnes ETag in Anführungszeichen kann passen. Ohne Anführungszeichen, schwach (W/…), <c>*</c> oder
/// eine Liste passen nie (412). Ein fehlender oder leerer Header gilt als fehlend (428).
/// </summary>
internal static class Etag
{
    /// <summary>Kann nie einem echten Stand entsprechen (Stände sind Ziffern).</summary>
    private const string PasstNie = "<kein gültiges ETag>";

    public static string Aus(string stand) => $"\"{stand}\"";

    public static string? StandAusIfMatch(string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return null;
        }

        var wert = ifMatch.Trim();
        var inneres = wert.Length >= 2 && wert[0] == '"' && wert[^1] == '"' ? wert[1..^1] : null;
        return inneres is null || inneres.Contains('"', StringComparison.Ordinal) ? PasstNie : inneres;
    }
}

/// <summary>Status im Vertrag: <c>entwurf</c> | <c>freigegeben</c>.</summary>
internal static class StatusText
{
    public static string Von(FassungsStatus status) => status switch
    {
        FassungsStatus.Entwurf => "entwurf",
        FassungsStatus.Freigegeben => "freigegeben",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unbekannter Fassungsstatus."),
    };
}
