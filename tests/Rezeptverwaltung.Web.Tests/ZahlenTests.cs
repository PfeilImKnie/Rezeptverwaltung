using Rezeptverwaltung.Web.Client;

namespace Rezeptverwaltung.Web.Tests;

public sealed class ZahlenTests
{
    [Theory]
    [InlineData("12,5", 12.5)]
    [InlineData("12.5", 12.5)]
    [InlineData(" 140 ", 140)]
    [InlineData("0,01", 0.01)]
    [InlineData("1000", 1000)]
    [InlineData("007", 7)]
    [InlineData("999999999999999", 999999999999999)]
    public void Liest_Zahlen_mit_Komma_oder_Punkt(string text, decimal erwartet)
    {
        Zahlen.Lesen(text, 2, out var fehler).ShouldBe(erwartet);
        fehler.ShouldBeNull();
    }

    [Theory]
    [InlineData("1.000", "Bitte ohne Tausenderpunkt eingeben, z. B. 1000.")] // wäre sonst still 1 g
    [InlineData("10.000", "Bitte ohne Tausenderpunkt eingeben, z. B. 10000.")]
    [InlineData("1.000.000", "Bitte ohne Tausenderpunkt eingeben, z. B. 1000000.")]
    [InlineData("1,005", "Höchstens 2 Stellen nach dem Komma.")]
    [InlineData("", "Bitte eine Zahl eingeben.")]
    [InlineData("   ", "Bitte eine Zahl eingeben.")]
    [InlineData(null, "Bitte eine Zahl eingeben.")]
    [InlineData("12,", "Bitte nur Ziffern und höchstens ein Komma eingeben, z. B. 12,5.")]
    [InlineData(",5", "Bitte nur Ziffern und höchstens ein Komma eingeben, z. B. 12,5.")]
    [InlineData("1,2,3", "Bitte nur Ziffern und höchstens ein Komma eingeben, z. B. 12,5.")]
    [InlineData("1.234,5", "Bitte nur Ziffern und höchstens ein Komma eingeben, z. B. 12,5.")]
    [InlineData("-5", "Bitte nur Ziffern und höchstens ein Komma eingeben, z. B. 12,5.")]
    [InlineData("+5", "Bitte nur Ziffern und höchstens ein Komma eingeben, z. B. 12,5.")]
    [InlineData("5 g", "Bitte nur Ziffern und höchstens ein Komma eingeben, z. B. 12,5.")]
    [InlineData("1e3", "Bitte nur Ziffern und höchstens ein Komma eingeben, z. B. 12,5.")]
    [InlineData("١٢", "Bitte nur Ziffern und höchstens ein Komma eingeben, z. B. 12,5.")] // arabisch-indische Ziffern
    [InlineData("1000000000000000", "Die Zahl ist zu groß.")]
    public void Weist_Unklares_ab(string? text, string meldung)
    {
        Zahlen.Lesen(text, 2, out var fehler).ShouldBeNull();
        fehler.ShouldBe(meldung);
    }

    [Theory]
    [InlineData("26,5", 1, 26.5)]
    [InlineData("26,55", 1, null)]
    [InlineData("26,5", 0, null)]
    [InlineData("26", 0, 26.0)]
    public void Nachkommastellen_je_Feld(string text, int stellen, double? erwartet) =>
        Zahlen.Lesen(text, stellen, out _).ShouldBe(erwartet is null ? null : (decimal)erwartet);

    [Fact]
    public void Meldung_fuer_eine_Nachkommastelle_und_ganze_Zahlen()
    {
        Zahlen.Lesen("1,23", 1, out var eine);
        eine.ShouldBe("Höchstens 1 Stelle nach dem Komma.");
        Zahlen.Lesen("1,2", 0, out var ganz);
        ganz.ShouldBe("Bitte eine ganze Zahl eingeben.");
    }

    [Theory]
    [InlineData(1234.5, "1.234,5")]
    [InlineData(140, "140")]
    [InlineData(0.01, "0,01")]
    [InlineData(67.10, "67,1")]
    [InlineData(1000000, "1.000.000")]
    public void Zeigt_Zahlen_deutsch_an(decimal wert, string erwartet) =>
        Zahlen.Anzeigen(wert).ShouldBe(erwartet);

    [Theory]
    [InlineData(1234.5, "1234,5")]
    [InlineData(12.25, "12,25")]
    public void Eingabe_ist_ohne_Tausenderpunkte_wieder_lesbar(decimal wert, string erwartet)
    {
        Zahlen.Eingabe(wert).ShouldBe(erwartet);
        Zahlen.Lesen(Zahlen.Eingabe(wert), 2, out _).ShouldBe(wert);
    }

    [Theory]
    [InlineData(0.479289940828, "0,4793")]
    [InlineData(2, "2")]
    [InlineData(0.00001, "0,00001")] // ein Faktor > 0 erscheint nie als „0“
    [InlineData(0.000000123456789012, "0,000000123456789012")]
    public void Faktor_mit_vier_Nachkommastellen(decimal wert, string erwartet) =>
        Zahlen.Faktor(wert).ShouldBe(erwartet);

    [Fact]
    public void Unabhaengig_von_der_Spracheinstellung()
    {
        var vorher = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Zahlen.Anzeigen(1234.5m).ShouldBe("1.234,5");
            Zahlen.Lesen("12,5", 2, out _).ShouldBe(12.5m);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = vorher;
        }
    }
}
