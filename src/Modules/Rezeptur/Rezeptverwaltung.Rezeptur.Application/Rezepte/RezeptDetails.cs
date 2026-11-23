using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

/// <summary>Lesemodell eines Rezepts für Frontends.</summary>
public sealed record RezeptDetails(Guid Id, string Name, IReadOnlyList<ZutatMenge> Zutaten, decimal GesamtgewichtGramm)
{
    internal static RezeptDetails Aus(Rezept rezept) => new(
        rezept.Id.Wert,
        rezept.Name.Wert,
        rezept.Zutaten.Select(ZutatMenge.Aus).ToList(),
        rezept.Gesamtgewicht.Gramm);
}

public sealed record ZutatMenge(string Bezeichnung, decimal Gramm)
{
    /// <summary>Ausgegebene Mengen werden auf 0,01 g gerundet. Intern wird ungerundet gerechnet.</summary>
    public const int AnzeigeNachkommastellen = 2;

    internal static ZutatMenge Aus(Zutatenzeile zeile) => new(
        zeile.Bezeichnung,
        decimal.Round(zeile.Menge.Gramm, AnzeigeNachkommastellen, MidpointRounding.AwayFromZero));
}
