using System.Globalization;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Rezepte;

/// <summary>
/// Suchtext für Rezeptnamen. Groß-/Kleinschreibung und Akzente zählen nicht („kase“ findet „Käsekuchen“), weil auf
/// dem Tablet in der Backstube schnell und ungenau getippt wird. Leerraum am Rand zählt nicht; leer findet alles.
/// </summary>
public sealed record Rezeptsuche
{
    public const int MaximaleLaenge = 100;

    private const CompareOptions Vergleich = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private Rezeptsuche(string text) => Text = text;

    /// <summary>Bereinigter Suchtext; leer, wenn alles gefunden werden soll.</summary>
    public string Text { get; }

    public static Rezeptsuche Von(string? text)
    {
        var roh = text ?? string.Empty;
        DomainRuleViolationException.Wenn(
            Freitext.Laenge(roh) > MaximaleLaenge,
            "suche.zu_lang",
            $"Ein Suchtext darf höchstens {MaximaleLaenge} Zeichen lang sein.");
        return new Rezeptsuche(Freitext.OhneLeerraumAmRand(roh));
    }

    public bool Passt(RezeptName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Text.Length == 0 || CultureInfo.InvariantCulture.CompareInfo.IndexOf(name.Wert, Text, Vergleich) >= 0;
    }

    /// <summary>Sortierung für Listen: alphabetisch, ohne Rücksicht auf Groß-/Kleinschreibung.</summary>
    public static IComparer<RezeptName> Reihenfolge { get; } = Comparer<RezeptName>.Create((a, b) =>
        CultureInfo.InvariantCulture.CompareInfo.Compare(a.Wert, b.Wert, CompareOptions.IgnoreCase) is var ergebnis and not 0
            ? ergebnis
            : string.CompareOrdinal(a.Wert, b.Wert));
}
