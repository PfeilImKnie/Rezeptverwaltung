using System.Globalization;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Rezepte;

/// <summary>
/// Gewicht in Gramm. In der Konditorei wird ausschließlich nach Gewicht gearbeitet.
/// Rechenergebnisse (z. B. beim Skalieren) behalten volle Genauigkeit; gerundet wird erst bei der Darstellung.
/// </summary>
public readonly record struct Gewicht : IComparable<Gewicht>
{
    /// <summary>Obergrenze für erfasste Gewichte: 1.000 t. Schützt vor Tippfehlern und Überläufen.</summary>
    public const decimal MaximalErfassbarGramm = 1_000_000_000m;

    /// <summary>Erfasste Gewichte werden auf 0,01 g genau angegeben.</summary>
    public const int ErfassungsNachkommastellen = 2;

    private Gewicht(decimal gramm)
    {
        DomainRuleViolationException.Wenn(gramm < 0, "gewicht.negativ", "Ein Gewicht darf nicht negativ sein.");
        Gramm = gramm;
    }

    public decimal Gramm { get; }

    public static Gewicht Null => new(0m);

    /// <summary>Gewicht, wie es ein Mensch erfasst (Rezepteingabe, Zielgewicht).</summary>
    public static Gewicht Erfasst(decimal gramm)
    {
        DomainRuleViolationException.Wenn(
            gramm > MaximalErfassbarGramm,
            "gewicht.zu_gross",
            $"Ein Gewicht darf höchstens {MaximalErfassbarGramm.ToString("N0", CultureInfo.GetCultureInfo("de-DE"))} g betragen.");
        DomainRuleViolationException.Wenn(
            decimal.Round(gramm, ErfassungsNachkommastellen) != gramm,
            "gewicht.zu_genau",
            "Gewichte werden auf 0,01 g genau erfasst.");
        return new Gewicht(gramm);
    }

    public bool IstNull => Gramm == 0m;

    public Gewicht Plus(Gewicht anderes) => new(Gramm + anderes.Gramm);

    public Gewicht Skaliert(Skalierungsfaktor faktor) => new(Gramm * faktor.Wert);

    public int CompareTo(Gewicht other) => Gramm.CompareTo(other.Gramm);

    public static Gewicht operator +(Gewicht links, Gewicht rechts) => links.Plus(rechts);

    public static bool operator <(Gewicht links, Gewicht rechts) => links.CompareTo(rechts) < 0;

    public static bool operator >(Gewicht links, Gewicht rechts) => links.CompareTo(rechts) > 0;

    public static bool operator <=(Gewicht links, Gewicht rechts) => links.CompareTo(rechts) <= 0;

    public static bool operator >=(Gewicht links, Gewicht rechts) => links.CompareTo(rechts) >= 0;

    public override string ToString() => $"{Gramm.ToString("0.##", CultureInfo.GetCultureInfo("de-DE"))} g";
}
