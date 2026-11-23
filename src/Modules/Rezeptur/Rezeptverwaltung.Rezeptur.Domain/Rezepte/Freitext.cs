namespace Rezeptverwaltung.Rezeptur.Domain.Rezepte;

/// <summary>
/// Regeln für Freitext (Namen, Bezeichnungen), exakt passend zum API-Vertrag (JSON Schema, ADR 0009):
/// <list type="bullet">
/// <item>Länge in Unicode-Codepoints (wie JSON Schema <c>maxLength</c>), gemessen <b>vor</b> dem Entfernen von Leerraum.</item>
/// <item>Leerraum ist ausdrücklich aufgezählt, nicht über <c>\s</c> definiert: <c>\s</c> bedeutet in ECMAScript, Python
/// und .NET jeweils etwas anderes. Die Menge ist die Vereinigung dieser Auslegungen und steht identisch im Vertragsmuster.</item>
/// </list>
/// </summary>
internal static class Freitext
{
    /// <summary>Anzahl Codepoints. Ungepaarte Surrogates zählen je als eins.</summary>
    public static int Laenge(string text) => text.EnumerateRunes().Count();

    /// <summary>
    /// U+0000 ist in Freitext unzulässig: PostgreSQL kann es weder in <c>text</c> noch in <c>jsonb</c> speichern (ADR 0011).
    /// </summary>
    public static bool EnthaeltNullzeichen(string text) => text.Contains('\0', StringComparison.Ordinal);

    public static string OhneLeerraumAmRand(string text)
    {
        var anfang = 0;
        var ende = text.Length;
        while (anfang < ende && IstLeerraum(text[anfang]))
        {
            anfang++;
        }

        while (ende > anfang && IstLeerraum(text[ende - 1]))
        {
            ende--;
        }

        return text[anfang..ende];
    }

    private static bool IstLeerraum(char zeichen) => zeichen is
        (>= (char)0x0009 and <= (char)0x000D) or (>= (char)0x001C and <= (char)0x001F) or (char)0x0020 or (char)0x0085 or (char)0x00A0 or (char)0x1680
        or (>= (char)0x2000 and <= (char)0x200A) or (char)0x2028 or (char)0x2029 or (char)0x202F or (char)0x205F
        or (char)0x3000 or (char)0xFEFF;
}
