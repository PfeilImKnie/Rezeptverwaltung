using System.Globalization;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Http;

/// <summary>
/// Gramm-Angabe aus Query-Parametern. Die Standardbindung von <see cref="decimal"/> akzeptiert
/// Tausendertrennzeichen und liest "1,5" als 15. In einer deutschen Backstube ist das ein stiller
/// Fehler um den Faktor 10. Erlaubt ist daher nur das Vertragsformat: optionales Vorzeichen, Ziffern
/// und Punkt als Dezimaltrennzeichen. Alles andere führt automatisch zu HTTP 400.
/// </summary>
internal readonly record struct Gramm(decimal Wert)
{
    private const NumberStyles Format = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public static bool TryParse(string? wert, IFormatProvider? provider, out Gramm gramm)
    {
        if (decimal.TryParse(wert, Format, CultureInfo.InvariantCulture, out var dezimal))
        {
            gramm = new Gramm(dezimal);
            return true;
        }

        gramm = default;
        return false;
    }
}
