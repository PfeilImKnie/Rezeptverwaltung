using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

public sealed class FassungMitFormTests
{
    private static Zutatenzeile Zeile(string bezeichnung, decimal gramm) => Zutatenzeile.Von(bezeichnung, Gewicht.Erfasst(gramm));

    private static Rezept Sachermasse(Bezugsform? form) => Rezept.Anlegen(
        RezeptName.Von("Sachermasse"),
        [Zeile("Butter", 140), Zeile("Zucker", 140), Zeile("Eier", 280)],
        Testzeit.Jetzt,
        form);

    [Fact]
    public void Neues_Rezept_traegt_die_Bezugsform_in_Fassung_1() =>
        Sachermasse(Bezugsform.Ring(26, 6)).MassgeblicheFassung.Bezugsform.ShouldBe(Bezugsform.Ring(26, 6));

    [Fact]
    public void Rezept_ohne_Form_ist_erlaubt() =>
        Sachermasse(null).MassgeblicheFassung.Bezugsform.ShouldBeNull();

    [Fact]
    public void Umrechnen_auf_kleineren_Ring_skaliert_alle_Mengen_mit_dem_Volumenfaktor()
    {
        var fassung = Sachermasse(Bezugsform.Ring(26, 6)).MassgeblicheFassung;

        var umrechnung = fassung.UmgerechnetAuf(Bezugsform.Ring(13, 6));

        umrechnung.Faktor.Wert.ShouldBe(0.25m);
        umrechnung.Zutaten.Select(z => z.Menge.Gramm).ShouldBe([35m, 35m, 70m]);
        fassung.Zutaten.Select(z => z.Menge.Gramm).ShouldBe([140m, 140m, 280m]);
    }

    [Fact]
    public void Ohne_Bezugsform_ist_Umrechnen_ein_Zustandskonflikt()
    {
        var verletzung = Should.Throw<DomainRuleViolationException>(
            () => Sachermasse(null).MassgeblicheFassung.UmgerechnetAuf(Bezugsform.Ring(18, 6)));

        verletzung.ShouldHaveCode("fassung.ohne_bezugsform");
        verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
    }

    [Fact]
    public void Ohne_Zutaten_ist_Umrechnen_ein_Zustandskonflikt() =>
        Should.Throw<DomainRuleViolationException>(
                () => Rezept.Anlegen(RezeptName.Von("Leer"), [], Testzeit.Jetzt, Bezugsform.Ring(26, 6)).MassgeblicheFassung.UmgerechnetAuf(Bezugsform.Ring(18, 6)))
            .ShouldHaveCode("rezept.ohne_zutaten");

    [Fact]
    public void Zielform_null_ist_ein_Programmierfehler() =>
        Should.Throw<ArgumentNullException>(() => Sachermasse(Bezugsform.Ring(26, 6)).MassgeblicheFassung.UmgerechnetAuf(null!));

    [Fact]
    public void Fehlende_Zielform_wird_vor_fachlichen_Zustaenden_gemeldet() =>
        // Programmierfehler (null) gehen vor 409, sonst verdeckte ein fachlicher Zustand den Fehler im Aufrufer.
        Should.Throw<ArgumentNullException>(() => Sachermasse(null).MassgeblicheFassung.UmgerechnetAuf(null!));

    [Fact]
    public void Neuer_Entwurf_uebernimmt_die_Bezugsform()
    {
        var rezept = Sachermasse(Bezugsform.Ring(26, 6));
        rezept.EntwurfFreigeben(Testzeit.Jetzt);

        rezept.EntwurfAnlegen(Testzeit.Jetzt).Bezugsform.ShouldBe(Bezugsform.Ring(26, 6));
    }

    [Fact]
    public void Bearbeiten_ersetzt_die_Bezugsform_ohne_die_freigegebene_Fassung_zu_aendern()
    {
        var rezept = Sachermasse(Bezugsform.Ring(26, 6));
        rezept.EntwurfFreigeben(Testzeit.Jetzt);
        rezept.EntwurfAnlegen(Testzeit.Jetzt);

        rezept.EntwurfBearbeiten(rezept.Entwurf!.Zutaten, Bezugsform.Blech(60, 40, 2));

        rezept.Entwurf!.Bezugsform.ShouldBe(Bezugsform.Blech(60, 40, 2));
        rezept.FassungMitNummer(1)!.Bezugsform.ShouldBe(Bezugsform.Ring(26, 6));
    }

    [Fact]
    public void Bearbeiten_ohne_Form_entfernt_die_Bezugsform_des_Entwurfs()
    {
        var rezept = Sachermasse(Bezugsform.Ring(26, 6));

        rezept.EntwurfBearbeiten(rezept.Entwurf!.Zutaten);

        rezept.Entwurf!.Bezugsform.ShouldBeNull();
    }
}
