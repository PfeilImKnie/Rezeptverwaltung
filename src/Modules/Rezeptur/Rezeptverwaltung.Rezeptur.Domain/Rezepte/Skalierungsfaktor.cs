using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Rezepte;

/// <summary>Faktor, mit dem alle Mengen eines Rezepts multipliziert werden. Immer größer als 0.</summary>
public readonly record struct Skalierungsfaktor
{
    private Skalierungsfaktor(decimal wert)
    {
        DomainRuleViolationException.Wenn(wert <= 0m, "skalierung.faktor_ungueltig", "Der Skalierungsfaktor muss größer als 0 sein.");
        Wert = wert;
    }

    public decimal Wert { get; }

    public static Skalierungsfaktor Von(decimal wert) => new(wert);

    /// <summary>Faktor, der <paramref name="ausgang"/> auf <paramref name="ziel"/> bringt.</summary>
    public static Skalierungsfaktor Zwischen(Gewicht ausgang, Gewicht ziel)
    {
        DomainRuleViolationException.Wenn(ausgang.IstNull, "skalierung.ausgang_null", "Ein Gewicht von 0 g kann nicht skaliert werden.");
        return new(ziel.Gramm / ausgang.Gramm);
    }
}
