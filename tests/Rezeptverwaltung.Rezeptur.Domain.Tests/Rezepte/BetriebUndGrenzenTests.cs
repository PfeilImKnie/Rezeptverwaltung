using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

/// <summary>Betrieb als Eigentümer (ADR 0015) und Obergrenze der Zutatenzeilen (Issue #6).</summary>
public sealed class BetriebUndGrenzenTests
{
    private static IEnumerable<Zutatenzeile> Zeilen(int anzahl) =>
        Enumerable.Range(1, anzahl).Select(i => Zutatenzeile.Von($"Zutat {i}", Gewicht.Erfasst(1)));

    [Fact]
    public void Rezept_gehoert_dem_Betrieb_aus_dem_Anlegen() =>
        Rezept.Anlegen(Testbetrieb.B, RezeptName.Von("Linzer"), [], Testzeit.Jetzt).BetriebId.ShouldBe(Testbetrieb.B);

    [Fact]
    public void Hundert_Zutatenzeilen_sind_erlaubt() =>
        Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Viel"), Zeilen(Fassung.MaximaleZutatenzeilen), Testzeit.Jetzt).Zutaten.Count.ShouldBe(100);

    [Fact]
    public void Hunderteins_Zutatenzeilen_sind_eine_ungueltige_Eingabe()
    {
        var verletzung = Should.Throw<DomainRuleViolationException>(
            () => Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Zu viel"), Zeilen(101), Testzeit.Jetzt));

        verletzung.ShouldHaveCode("fassung.zu_viele_zutaten");
        verletzung.Art.ShouldBe(Regelart.UngueltigeEingabe);
    }

    [Fact]
    public void Auch_beim_Bearbeiten_gilt_die_Obergrenze()
    {
        var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Probe"), [], Testzeit.Jetzt);

        Should.Throw<DomainRuleViolationException>(() => rezept.EntwurfBearbeiten(Zeilen(101))).ShouldHaveCode("fassung.zu_viele_zutaten");
        rezept.Zutaten.ShouldBeEmpty();
    }

    [Fact]
    public void Hinzufuegen_ueber_die_Obergrenze_ist_ein_Zustandskonflikt()
    {
        var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Voll"), Zeilen(100), Testzeit.Jetzt);

        var verletzung = Should.Throw<DomainRuleViolationException>(() => rezept.ZutatHinzufuegen(Zutatenzeile.Von("Eine zu viel", Gewicht.Erfasst(1))));

        verletzung.ShouldHaveCode("fassung.zu_viele_zutaten");
        verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
        rezept.Zutaten.Count.ShouldBe(100);
    }

    [Fact]
    public void Neunundneunzig_plus_eins_ist_erlaubt()
    {
        var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Fast voll"), Zeilen(99), Testzeit.Jetzt);

        rezept.ZutatHinzufuegen(Zutatenzeile.Von("Letzte", Gewicht.Erfasst(1)));

        rezept.Zutaten.Count.ShouldBe(100);
    }

    [Fact]
    public void Neuer_Entwurf_aus_einer_Fassung_mit_genau_hundert_Zeilen_ist_erlaubt()
    {
        var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Voll"), Zeilen(100), Testzeit.Jetzt);
        rezept.EntwurfFreigeben(Testzeit.Jetzt);

        rezept.EntwurfAnlegen(Testzeit.Jetzt).Zutaten.Count.ShouldBe(100);
    }

    [Fact]
    public void Neuer_Entwurf_aus_einer_Altfassung_ueber_der_Grenze_ist_ein_Zustandskonflikt()
    {
        var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Alt"), Zeilen(100), Testzeit.Jetzt);
        rezept.EntwurfFreigeben(Testzeit.Jetzt);

        // Simuliert, was EF beim Laden einer Fassung aus der Zeit vor der Grenze tut: das private Feld direkt füllen.
        var feld = typeof(Fassung).GetField("_zutaten", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        ((List<Zutatenzeile>)feld.GetValue(rezept.AktuelleFassung)!).Add(Zutatenzeile.Von("Nummer 101", Gewicht.Erfasst(1)));

        var verletzung = Should.Throw<DomainRuleViolationException>(() => rezept.EntwurfAnlegen(Testzeit.Jetzt));

        verletzung.ShouldHaveCode("fassung.zu_viele_zutaten");
        verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
        rezept.Entwurf.ShouldBeNull();
    }

    [Theory]
    [InlineData("123456789012345678")]
    [InlineData("betrieb-a")]
    [InlineData("A_b-9")]
    public void Gueltige_Betriebs_IDs(string wert) =>
        BetriebId.Von(wert).Wert.ShouldBe(wert);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mit leerzeichen")]
    [InlineData("ümlaut")]
    [InlineData("a/b")]
    public void Ungueltige_Betriebs_IDs(string? wert) =>
        Should.Throw<ArgumentException>(() => BetriebId.Von(wert));

    [Fact]
    public void Betriebs_ID_ist_hoechstens_64_Zeichen_lang()
    {
        BetriebId.Von(new string('a', 64)).Wert.Length.ShouldBe(64);
        Should.Throw<ArgumentException>(() => BetriebId.Von(new string('a', 65)));
    }

    [Fact]
    public void Betriebs_ID_als_Text() =>
        BetriebId.Von("123").ToString().ShouldBe("123");
}
