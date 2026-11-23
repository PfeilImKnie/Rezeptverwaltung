using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

/// <summary>Lesemodell eines Rezepts. Zutaten und Gesamtgewicht beziehen sich auf die maßgebliche Fassung.</summary>
public sealed record RezeptDetails(
    Guid Id,
    string Name,
    IReadOnlyList<ZutatMenge> Zutaten,
    decimal GesamtgewichtGramm,
    Form? Bezugsform,
    int MassgeblicheFassung,
    int? AktuelleFassung,
    int? Entwurf,
    IReadOnlyList<FassungUebersicht> Fassungen,
    string Stand)
{
    internal static RezeptDetails Aus(Rezept rezept, string stand) => new(
        rezept.Id.Wert,
        rezept.Name.Wert,
        rezept.Zutaten.Select(ZutatMenge.Aus).ToList(),
        rezept.Gesamtgewicht.Gramm,
        Form.Aus(rezept.MassgeblicheFassung.Bezugsform),
        rezept.MassgeblicheFassung.Nummer,
        rezept.AktuelleFassung?.Nummer,
        rezept.Entwurf?.Nummer,
        rezept.Fassungen.OrderBy(f => f.Nummer).Select(FassungUebersicht.Aus).ToList(),
        stand);
}

public sealed record FassungUebersicht(int Nummer, FassungsStatus Status, DateTimeOffset ErstelltAm, DateTimeOffset? FreigegebenAm)
{
    internal static FassungUebersicht Aus(Fassung fassung) =>
        new(fassung.Nummer, fassung.Status, fassung.ErstelltAm, fassung.FreigegebenAm);
}

public sealed record FassungDetails(
    Guid RezeptId,
    int Nummer,
    FassungsStatus Status,
    DateTimeOffset ErstelltAm,
    DateTimeOffset? FreigegebenAm,
    Form? Bezugsform,
    IReadOnlyList<ZutatMenge> Zutaten,
    decimal GesamtgewichtGramm,
    string Stand)
{
    internal static FassungDetails Aus(Rezept rezept, Fassung fassung, string stand) => new(
        rezept.Id.Wert,
        fassung.Nummer,
        fassung.Status,
        fassung.ErstelltAm,
        fassung.FreigegebenAm,
        Form.Aus(fassung.Bezugsform),
        fassung.Zutaten.Select(ZutatMenge.Aus).ToList(),
        fassung.Gesamtgewicht.Gramm,
        stand);
}

public sealed record ZutatMenge(string Bezeichnung, decimal Gramm)
{
    /// <summary>Ausgegebene Mengen werden auf 0,01 g gerundet. Intern wird ungerundet gerechnet.</summary>
    public const int AnzeigeNachkommastellen = 2;

    internal static ZutatMenge Aus(Zutatenzeile zeile) => new(
        zeile.Bezeichnung,
        decimal.Round(zeile.Menge.Gramm, AnzeigeNachkommastellen, MidpointRounding.AwayFromZero));
}
