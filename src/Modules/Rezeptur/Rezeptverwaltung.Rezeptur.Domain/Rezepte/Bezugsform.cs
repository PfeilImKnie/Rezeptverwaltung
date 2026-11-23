using System.Globalization;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Rezepte;

public enum Formart
{
    /// <summary>Runder Tortenring oder Springform.</summary>
    Ring,

    /// <summary>Blech oder rechteckiger Rahmen.</summary>
    Blech,
}

/// <summary>
/// Form, für die eine Fassung geschrieben ist (ADR 0014). Maße in cm auf 0,1 cm genau. Umgerechnet wird über das
/// Volumen, weil die Höhe fachlich zählt (Ring 26 × 6 cm braucht weniger Masse als 26 × 8 cm).
/// </summary>
public sealed record Bezugsform
{
    public const decimal MinimalesMassCm = 1m;
    public const decimal MaximalesMassCm = 200m;
    public const decimal MinimaleHoeheCm = 0.1m;
    public const decimal MaximaleHoeheCm = 50m;
    public const int Nachkommastellen = 1;

    private const decimal Pi = 3.1415926535897932384626433833m;

    private Bezugsform(Formart art, decimal? durchmesserCm, decimal? laengeCm, decimal? breiteCm, decimal hoeheCm)
    {
        Art = art;
        DurchmesserCm = durchmesserCm;
        LaengeCm = laengeCm;
        BreiteCm = breiteCm;
        HoeheCm = hoeheCm;
    }

    public Formart Art { get; }

    /// <summary>Nur beim Ring.</summary>
    public decimal? DurchmesserCm { get; }

    /// <summary>Nur beim Blech.</summary>
    public decimal? LaengeCm { get; }

    /// <summary>Nur beim Blech.</summary>
    public decimal? BreiteCm { get; }

    public decimal HoeheCm { get; }

    public decimal VolumenCm3 => Art == Formart.Ring
        ? Pi * (DurchmesserCm!.Value / 2) * (DurchmesserCm.Value / 2) * HoeheCm
        : LaengeCm!.Value * BreiteCm!.Value * HoeheCm;

    public static Bezugsform Ring(decimal durchmesserCm, decimal hoeheCm) =>
        new(Formart.Ring, Mass(durchmesserCm, "Durchmesser"), laengeCm: null, breiteCm: null, Hoehe(hoeheCm));

    public static Bezugsform Blech(decimal laengeCm, decimal breiteCm, decimal hoeheCm) =>
        new(Formart.Blech, durchmesserCm: null, Mass(laengeCm, "Länge"), Mass(breiteCm, "Breite"), Hoehe(hoeheCm));

    /// <summary>Faktor, mit dem alle Mengen multipliziert werden, um das Rezept für die Zielform zu erhalten.</summary>
    public Skalierungsfaktor FaktorFuer(Bezugsform zielform)
    {
        ArgumentNullException.ThrowIfNull(zielform);
        return Skalierungsfaktor.Von(zielform.VolumenCm3 / VolumenCm3);
    }

    public override string ToString() => Art == Formart.Ring
        ? $"Ring Ø {Cm(DurchmesserCm!.Value)} × {Cm(HoeheCm)} cm"
        : $"Blech {Cm(LaengeCm!.Value)} × {Cm(BreiteCm!.Value)} × {Cm(HoeheCm)} cm";

    private static string Cm(decimal wert) => wert.ToString("0.#", CultureInfo.GetCultureInfo("de-DE"));

    private static decimal Mass(decimal cm, string bezeichnung)
    {
        DomainRuleViolationException.Wenn(
            cm < MinimalesMassCm || cm > MaximalesMassCm,
            "form.mass_ungueltig",
            $"{bezeichnung} muss zwischen {Cm(MinimalesMassCm)} und {Cm(MaximalesMassCm)} cm liegen.");
        return Genau(cm, bezeichnung);
    }

    private static decimal Hoehe(decimal cm)
    {
        DomainRuleViolationException.Wenn(
            cm < MinimaleHoeheCm || cm > MaximaleHoeheCm,
            "form.hoehe_ungueltig",
            $"Die Höhe muss zwischen {Cm(MinimaleHoeheCm)} und {Cm(MaximaleHoeheCm)} cm liegen.");
        return Genau(cm, "Höhe");
    }

    private static decimal Genau(decimal cm, string bezeichnung)
    {
        DomainRuleViolationException.Wenn(
            decimal.Round(cm, Nachkommastellen) != cm,
            "form.zu_genau",
            $"{bezeichnung} wird auf 0,1 cm genau angegeben.");
        return cm;
    }
}
