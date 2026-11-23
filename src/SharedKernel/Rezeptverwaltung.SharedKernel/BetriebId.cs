namespace Rezeptverwaltung.SharedKernel;

/// <summary>
/// Mandant (ADR 0015): ein Betrieb, z. B. eine Konditorei. Entspricht der Organisation im Identity Provider.
/// Erlaubt sind 1–64 Zeichen aus Buchstaben, Ziffern, Bindestrich und Unterstrich (Zitadel-Org-IDs sind Ziffern).
/// </summary>
public readonly record struct BetriebId
{
    public const int MaximaleLaenge = 64;

    private BetriebId(string wert) => Wert = wert;

    public string Wert { get; }

    public static BetriebId Von(string? wert)
    {
        if (string.IsNullOrEmpty(wert) || wert.Length > MaximaleLaenge || !wert.All(ErlaubtesZeichen))
        {
            throw new ArgumentException($"Ungültige Betriebs-ID '{wert}'.", nameof(wert));
        }

        return new BetriebId(wert);
    }

    public override string ToString() => Wert;

    private static bool ErlaubtesZeichen(char zeichen) => char.IsAsciiLetterOrDigit(zeichen) || zeichen is '-' or '_';
}
