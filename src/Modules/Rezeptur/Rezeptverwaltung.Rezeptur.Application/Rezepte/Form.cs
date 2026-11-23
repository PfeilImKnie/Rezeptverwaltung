using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

/// <summary>Bezugsform als Ein- und Ausgabemodell. Felder, die zur Art nicht passen, werden ignoriert bzw. sind null.</summary>
public sealed record Form(Formart Art, decimal? DurchmesserCm, decimal? LaengeCm, decimal? BreiteCm, decimal HoeheCm)
{
    public static Form Ring(decimal durchmesserCm, decimal hoeheCm) => new(Formart.Ring, durchmesserCm, null, null, hoeheCm);

    public static Form Blech(decimal laengeCm, decimal breiteCm, decimal hoeheCm) => new(Formart.Blech, null, laengeCm, breiteCm, hoeheCm);

    internal Bezugsform AlsBezugsform() => Art switch
    {
        Formart.Ring => Bezugsform.Ring(Pflicht(DurchmesserCm, "durchmesserCm"), HoeheCm),
        Formart.Blech => Bezugsform.Blech(Pflicht(LaengeCm, "laengeCm"), Pflicht(BreiteCm, "breiteCm"), HoeheCm),
        _ => throw new ArgumentOutOfRangeException(nameof(Art), Art, "Unbekannte Formart."),
    };

    internal static Form? Aus(Bezugsform? bezugsform) => bezugsform is null
        ? null
        : new Form(bezugsform.Art, bezugsform.DurchmesserCm, bezugsform.LaengeCm, bezugsform.BreiteCm, bezugsform.HoeheCm);

    /// <summary>Fehlende Pflichtmaße sind ein Formatfehler des Aufrufers (der HTTP-Adapter prüft das bereits).</summary>
    private static decimal Pflicht(decimal? wert, string feld) =>
        wert ?? throw new ArgumentException($"Für die Formart fehlt '{feld}'.", feld);
}
