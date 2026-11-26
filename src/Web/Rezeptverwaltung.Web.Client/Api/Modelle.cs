using System.Text.Json.Serialization;

namespace Rezeptverwaltung.Web.Client.Api;

// Abbild des API-Vertrags (api/openapi.yaml). Feldnamen werden in camelCase übertragen.

public sealed record RezeptUebersicht(Guid Id, string Name, int MassgeblicheFassung, int? AktuelleFassung, int? Entwurf);

public sealed record Berechtigungen(bool Lesen, bool Bearbeiten, bool Freigeben)
{
    public static readonly Berechtigungen Keine = new(false, false, false);
}

public sealed record Zutat(string Bezeichnung, decimal Gramm);

/// <summary>Bezugsform: <c>art</c> = <c>ring</c> (Durchmesser) oder <c>blech</c> (Länge, Breite); Höhe immer.</summary>
public sealed record Form(string Art, decimal HoeheCm, decimal? DurchmesserCm = null, decimal? LaengeCm = null, decimal? BreiteCm = null)
{
    public const string Ring = "ring";
    public const string Blech = "blech";

    public static Form AlsRing(decimal durchmesserCm, decimal hoeheCm) => new(Ring, hoeheCm, DurchmesserCm: durchmesserCm);

    public static Form AlsBlech(decimal laengeCm, decimal breiteCm, decimal hoeheCm) => new(Blech, hoeheCm, LaengeCm: laengeCm, BreiteCm: breiteCm);

    /// <summary>Für die Anzeige, z. B. „Ring Ø 26 cm, 6 cm hoch“. Wird nicht übertragen.</summary>
    [JsonIgnore]
    public string Beschreibung => Art == Ring
        ? $"Ring Ø {Zahlen.Anzeigen(DurchmesserCm ?? 0)} cm, {Zahlen.Anzeigen(HoeheCm)} cm hoch"
        : $"Blech {Zahlen.Anzeigen(LaengeCm ?? 0)} × {Zahlen.Anzeigen(BreiteCm ?? 0)} cm, {Zahlen.Anzeigen(HoeheCm)} cm hoch";
}

public sealed record FassungUebersicht(int Nummer, string Status, DateTimeOffset ErstelltAm, DateTimeOffset? FreigegebenAm)
{
    [JsonIgnore]
    public bool IstEntwurf => Status == "entwurf";
}

public sealed record RezeptDetails(
    Guid Id,
    string Name,
    IReadOnlyList<Zutat> Zutaten,
    decimal GesamtgewichtGramm,
    Form? Bezugsform,
    int MassgeblicheFassung,
    int? AktuelleFassung,
    int? Entwurf,
    IReadOnlyList<FassungUebersicht> Fassungen);

public sealed record FassungDetails(
    Guid RezeptId,
    int Nummer,
    string Status,
    DateTimeOffset ErstelltAm,
    DateTimeOffset? FreigegebenAm,
    Form? Bezugsform,
    IReadOnlyList<Zutat> Zutaten,
    decimal GesamtgewichtGramm)
{
    [JsonIgnore]
    public bool IstEntwurf => Status == "entwurf";
}

public sealed record SkaliertesRezept(
    Guid RezeptId,
    string Name,
    int Fassung,
    decimal Faktor,
    Form? Zielform,
    decimal ZielgewichtGramm,
    IReadOnlyList<Zutat> Zutaten);

public sealed record RezeptInhalt(IReadOnlyList<Zutat> Zutaten, Form? Bezugsform);

/// <summary>Antwort mit dem Stand des Rezepts (ETag), der bei Änderungen als If-Match zurückgeht (ADR 0013).</summary>
public sealed record MitStand<T>(T Wert, string Stand);

/// <summary>Worauf umgerechnet wird.</summary>
public abstract record Umrechnung
{
    public sealed record AufGewicht(decimal Gramm) : Umrechnung;

    public sealed record AufForm(Form Form) : Umrechnung;
}
