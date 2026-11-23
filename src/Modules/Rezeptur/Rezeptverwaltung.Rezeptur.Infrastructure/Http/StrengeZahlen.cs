using System.Globalization;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Http;

/// <summary>
/// Strenges Zahlenformat für Query-Parameter (ADR 0009). Die Standardbindung von <see cref="decimal"/> akzeptiert
/// Tausendertrennzeichen und liest "1,5" als 15, in einer deutschen Backstube ein stiller Fehler um den Faktor 10.
/// Erlaubt ist nur: optionales Vorzeichen, Ziffern, Punkt als Dezimaltrennzeichen. Alles andere führt zu HTTP 400.
/// </summary>
internal static class StrengeZahl
{
    private const NumberStyles Format = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public static bool TryParse(string? wert, out decimal zahl) =>
        decimal.TryParse(wert, Format, CultureInfo.InvariantCulture, out zahl);
}

/// <summary>Gramm-Angabe aus Query-Parametern, streng geparst.</summary>
internal readonly record struct Gramm(decimal Wert)
{
    public static bool TryParse(string? wert, IFormatProvider? provider, out Gramm gramm)
    {
        var ok = StrengeZahl.TryParse(wert, out var zahl);
        gramm = new Gramm(zahl);
        return ok;
    }
}

/// <summary>Zentimeter-Angabe aus Query-Parametern, streng geparst.</summary>
internal readonly record struct Zentimeter(decimal Wert)
{
    public static bool TryParse(string? wert, IFormatProvider? provider, out Zentimeter zentimeter)
    {
        var ok = StrengeZahl.TryParse(wert, out var zahl);
        zentimeter = new Zentimeter(zahl);
        return ok;
    }
}
