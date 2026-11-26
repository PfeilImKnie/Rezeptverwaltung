using System.Globalization;

namespace Rezeptverwaltung.Web.Client;

/// <summary>
/// Zahlen wie in der Backstube: Komma als Dezimaltrenner („12,5“), Punkt wird auch angenommen. Tausenderpunkte gibt
/// es bei der Eingabe nicht: „1.000“ hätte drei Nachkommastellen und wird abgewiesen, statt still als 1 g zu gelten.
/// Unabhängig von der Spracheinstellung des Browsers.
/// </summary>
public static partial class Zahlen
{
    private static readonly NumberFormatInfo Deutsch = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NegativeSign = "-",
    };

    /// <summary>Liest eine positive Zahl mit höchstens <paramref name="nachkommastellen"/> Stellen nach dem Komma.</summary>
    /// <returns>Die Zahl oder <c>null</c> mit <paramref name="fehler"/>.</returns>
    public static decimal? Lesen(string? text, int nachkommastellen, out string? fehler)
    {
        var roh = (text ?? string.Empty).Trim();
        fehler = null;
        if (roh.Length == 0)
        {
            fehler = "Bitte eine Zahl eingeben.";
            return null;
        }

        if (nachkommastellen < 3 && Tausenderschreibweise().IsMatch(roh))
        {
            fehler = $"Bitte ohne Tausenderpunkt eingeben, z. B. {roh.Replace(".", string.Empty, StringComparison.Ordinal)}.";
            return null;
        }

        var trenner = roh.IndexOfAny([',', '.']);
        var ganz = trenner < 0 ? roh : roh[..trenner];
        var bruch = trenner < 0 ? string.Empty : roh[(trenner + 1)..];
        if (ganz.Length == 0 || !ganz.All(char.IsAsciiDigit) || !bruch.All(char.IsAsciiDigit) || (trenner >= 0 && bruch.Length == 0))
        {
            fehler = "Bitte nur Ziffern und höchstens ein Komma eingeben, z. B. 12,5.";
            return null;
        }

        if (bruch.Length > nachkommastellen)
        {
            fehler = nachkommastellen == 0
                ? "Bitte eine ganze Zahl eingeben."
                : $"Höchstens {nachkommastellen} Stelle{(nachkommastellen == 1 ? string.Empty : "n")} nach dem Komma.";
            return null;
        }

        if (ganz.Length > 15)
        {
            fehler = "Die Zahl ist zu groß.";
            return null;
        }

        return decimal.Parse(bruch.Length == 0 ? ganz : $"{ganz}.{bruch}", NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    /// <summary>„1.000“, „10.000“, „1.000.000“: so getippt ist es fast immer ein Tausenderpunkt.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"^[0-9]{1,3}(\.[0-9]{3})+$")]
    private static partial System.Text.RegularExpressions.Regex Tausenderschreibweise();

    /// <summary>Für Anzeigen: „1.234,5“, ohne überflüssige Nullen, höchstens zwei Nachkommastellen.</summary>
    public static string Anzeigen(decimal wert) => wert.ToString("#,0.##", Deutsch);

    /// <summary>Für Eingabefelder: wie <see cref="Anzeigen"/>, aber ohne Tausenderpunkte, damit es wieder lesbar ist.</summary>
    public static string Eingabe(decimal wert) => wert.ToString("0.##", Deutsch);

    /// <summary>
    /// Faktor mit bis zu vier Nachkommastellen, z. B. „0,4793“. Kleinere Faktoren mit allen Stellen (die API rundet
    /// auf 12 signifikante Stellen), damit ein Faktor größer 0 nie als „0“ erscheint.
    /// </summary>
    public static string Faktor(decimal wert) =>
        wert.ToString(wert is > 0 and < 0.0001m ? "0.############################" : "#,0.####", Deutsch);
}
