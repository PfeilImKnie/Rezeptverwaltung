using CsCheck;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

/// <summary>
/// Unabhängiger Test der Bezugsform aus Sicht der Backstube: Grenzwerte je Maß und Formart, Extremfaktoren,
/// Eigenschaften der Umrechnung auf Ebene der Fassung und der Lebenszyklus der Form über Fassungen hinweg.
/// </summary>
public sealed class BezugsformGrenzfallTests
{
    private static readonly Gen<decimal> Mass = Gen.Int[10, 2000].Select(zehntel => zehntel / 10m);
    private static readonly Gen<decimal> Hoehe = Gen.Int[1, 500].Select(zehntel => zehntel / 10m);
    private static readonly Gen<Bezugsform> Form = Gen.OneOf(
        Gen.Select(Mass, Hoehe, Bezugsform.Ring),
        Gen.Select(Mass, Mass, Hoehe, Bezugsform.Blech));

    private static Zutatenzeile Zeile(string bezeichnung, decimal gramm) => Zutatenzeile.Von(bezeichnung, Gewicht.Erfasst(gramm));

    private static Rezept MitForm(Bezugsform? form, params Zutatenzeile[] zeilen) =>
        Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Sachertorte"), zeilen, Testzeit.Jetzt, form);

    // ---- Grenzwerte je Maß und Formart ----

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void Ringdurchmesser_an_den_Grenzen_ist_gueltig(decimal cm) =>
        Bezugsform.Ring(cm, 5).DurchmesserCm.ShouldBe(cm);

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void Blechbreite_an_den_Grenzen_ist_gueltig(decimal cm) =>
        Bezugsform.Blech(30, cm, 5).BreiteCm.ShouldBe(cm);

    [Theory]
    [InlineData(0.1)]
    [InlineData(50)]
    public void Blechhoehe_an_den_Grenzen_ist_gueltig(decimal cm) =>
        Bezugsform.Blech(60, 40, cm).HoeheCm.ShouldBe(cm);

    [Theory]
    [InlineData(0.99)]
    [InlineData(200.01)]
    public void Mass_knapp_ausserhalb_des_Bereichs_ist_ungueltig(decimal cm) =>
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Ring(cm, 5)).ShouldHaveCode("form.mass_ungueltig");

    [Theory]
    [InlineData(0.09)]
    [InlineData(50.01)]
    [InlineData(0)]
    [InlineData(50.1)]
    public void Blechhoehe_ausserhalb_des_Bereichs_ist_ungueltig(decimal cm) =>
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Blech(60, 40, cm)).ShouldHaveCode("form.hoehe_ungueltig");

    [Theory]
    [InlineData(60.05, 40, 2)]
    [InlineData(60, 40.05, 2)]
    [InlineData(60, 40, 2.05)]
    [InlineData(199.95, 40, 2)]
    public void Blechmasse_genauer_als_ein_Millimeter_sind_ungueltig(decimal laenge, decimal breite, decimal hoehe) =>
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Blech(laenge, breite, hoehe)).ShouldHaveCode("form.zu_genau");

    [Fact]
    public void Formmasse_sind_ungueltige_Eingaben_und_keine_Zustandskonflikte()
    {
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Ring(0.5m, 5)).Art.ShouldBe(Regelart.UngueltigeEingabe);
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Ring(20, 51)).Art.ShouldBe(Regelart.UngueltigeEingabe);
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Ring(20.05m, 5)).Art.ShouldBe(Regelart.UngueltigeEingabe);
    }

    [Fact]
    public void Nachkommanullen_sind_keine_hoehere_Genauigkeit() =>
        Bezugsform.Ring(18.50m, 6.00m).ShouldBe(Bezugsform.Ring(18.5m, 6));

    // ---- Fachliche Szenarien ----

    [Fact]
    public void Sachertorte_von_Ring_26_auf_Ring_18_umrechnen()
    {
        var fassung = MitForm(Bezugsform.Ring(26, 6), Zeile("Butter", 140), Zeile("Zucker", 140), Zeile("Eier", 280)).MassgeblicheFassung;

        var umrechnung = fassung.UmgerechnetAuf(Bezugsform.Ring(18, 6));

        // (18/26)² = 0,479289940828…
        umrechnung.Zutaten.Select(z => decimal.Round(z.Menge.Gramm, 2)).ShouldBe([67.10m, 67.10m, 134.20m]);
    }

    [Fact]
    public void Blech_auf_gleich_grosses_Blech_mit_doppelter_Hoehe_verdoppelt_die_Mengen()
    {
        var fassung = MitForm(Bezugsform.Blech(60, 40, 1.5m), Zeile("Eier", 500), Zeile("Mehl", 150)).MassgeblicheFassung;

        var umrechnung = fassung.UmgerechnetAuf(Bezugsform.Blech(60, 40, 3));

        umrechnung.Faktor.Wert.ShouldBe(2m);
        umrechnung.Zutaten.Select(z => z.Menge.Gramm).ShouldBe([1000m, 300m]);
    }

    [Fact]
    public void Blech_halbiert_auf_Rahmen_mit_halber_Laenge()
    {
        var fassung = MitForm(Bezugsform.Blech(60, 40, 2), Zeile("Eier", 501)).MassgeblicheFassung;

        fassung.UmgerechnetAuf(Bezugsform.Blech(30, 40, 2)).Zutaten.ShouldHaveSingleItem().Menge.Gramm.ShouldBe(250.5m);
    }

    [Fact]
    public void Kleinste_Form_auf_groesste_Form_mit_groesster_Menge_laeuft_nicht_ueber()
    {
        var fassung = MitForm(Bezugsform.Ring(1, 0.1m), Zeile("Zucker", 1_000_000_000m)).MassgeblicheFassung;

        var umrechnung = fassung.UmgerechnetAuf(Bezugsform.Blech(200, 200, 50));

        // 2.000.000 cm³ / (π · 0,25 · 0,1 cm³) ≈ 25.464.790,9
        Math.Abs(umrechnung.Faktor.Wert - 25_464_790.89m).ShouldBeLessThan(0.01m);
        umrechnung.Zutaten.ShouldHaveSingleItem().Menge.Gramm.ShouldBeGreaterThan(2.5e16m);
    }

    [Fact]
    public void Groesste_Form_auf_kleinste_Form_mit_kleinster_Menge_bleibt_positiv()
    {
        var fassung = MitForm(Bezugsform.Blech(200, 200, 50), Zeile("Salz", 0.01m)).MassgeblicheFassung;

        var umrechnung = fassung.UmgerechnetAuf(Bezugsform.Ring(1, 0.1m));

        umrechnung.Faktor.Wert.ShouldBeGreaterThan(0m);
        umrechnung.Zutaten.ShouldHaveSingleItem().Menge.Gramm.ShouldBeGreaterThan(0m);
    }

    [Fact]
    public void Doppelte_Zutaten_werden_beim_Umrechnen_einzeln_umgerechnet()
    {
        var fassung = MitForm(Bezugsform.Ring(26, 6), Zeile("Zucker", 100), Zeile("Zucker", 60)).MassgeblicheFassung;

        fassung.UmgerechnetAuf(Bezugsform.Ring(13, 6)).Zutaten.Select(z => (z.Bezeichnung, z.Menge.Gramm))
            .ShouldBe([("Zucker", 25m), ("Zucker", 15m)]);
    }

    // ---- Eigenschaften (property-based) ----

    [Fact]
    public void Eigenschaft_Umrechnen_erhaelt_die_Verhaeltnisse_und_die_Fassung() =>
        Gen.Select(Form, Form, Generatoren.Zutatenzeile.List[1, 15]).Sample((bezug, ziel, zeilen) =>
        {
            var fassung = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Probe"), zeilen, Testzeit.Jetzt, bezug).MassgeblicheFassung;
            var vorher = fassung.Zutaten.Select(z => z.Menge.Gramm).ToList();

            var umrechnung = fassung.UmgerechnetAuf(ziel);

            umrechnung.Zutaten.Count.ShouldBe(zeilen.Count);
            for (var i = 0; i < zeilen.Count; i++)
            {
                umrechnung.Zutaten[i].Bezeichnung.ShouldBe(fassung.Zutaten[i].Bezeichnung);
                umrechnung.Zutaten[i].Menge.Gramm.ShouldBe(vorher[i] * umrechnung.Faktor.Wert);
            }

            fassung.Zutaten.Select(z => z.Menge.Gramm).ShouldBe(vorher);
            fassung.Bezugsform.ShouldBe(bezug);
        });

    [Fact]
    public void Eigenschaft_Faktor_ist_Verhaeltnis_der_Volumen() =>
        Gen.Select(Form, Form).Sample((bezug, ziel) =>
        {
            var faktor = bezug.FaktorFuer(ziel).Wert;
            Math.Abs((faktor * bezug.VolumenCm3) - ziel.VolumenCm3).ShouldBeLessThan(ziel.VolumenCm3 * 0.000_000_001m);
        });

    [Fact]
    public void Eigenschaft_Hoehe_geht_linear_in_den_Faktor_ein() =>
        Gen.Select(Form, Mass, Gen.Int[1, 250]).Sample((bezug, durchmesser, zehntel) =>
        {
            var hoehe = zehntel / 10m;
            var einfach = bezug.FaktorFuer(Bezugsform.Ring(durchmesser, hoehe)).Wert;
            var doppelt = bezug.FaktorFuer(Bezugsform.Ring(durchmesser, hoehe * 2)).Wert;
            Math.Abs(doppelt - (2 * einfach)).ShouldBeLessThan(doppelt * 0.000_000_001m);
        });

    [Fact]
    public void Eigenschaft_groessere_Zielform_braucht_mehr_Masse() =>
        Gen.Select(Form, Gen.Int[10, 1999].Select(zehntel => zehntel / 10m), Hoehe).Sample((bezug, durchmesser, hoehe) =>
            bezug.FaktorFuer(Bezugsform.Ring(durchmesser + 0.1m, hoehe)).Wert
                .ShouldBeGreaterThan(bezug.FaktorFuer(Bezugsform.Ring(durchmesser, hoehe)).Wert));

    // ---- Lebenszyklus der Form über Fassungen ----

    [Fact]
    public void Freigabe_behaelt_die_Bezugsform()
    {
        var rezept = MitForm(Bezugsform.Blech(60, 40, 2), Zeile("Eier", 500));

        rezept.EntwurfFreigeben(Testzeit.Jetzt).Bezugsform.ShouldBe(Bezugsform.Blech(60, 40, 2));
    }

    [Fact]
    public void Neuer_Entwurf_einer_Fassung_ohne_Form_hat_keine_Form()
    {
        var rezept = MitForm(null, Zeile("Sahne", 500));
        rezept.EntwurfFreigeben(Testzeit.Jetzt);

        rezept.EntwurfAnlegen(Testzeit.Jetzt).Bezugsform.ShouldBeNull();
    }

    [Fact]
    public void Form_erst_im_Entwurf_ergaenzt_laesst_die_freigegebene_Fassung_ohne_Form()
    {
        var rezept = MitForm(null, Zeile("Eier", 300));
        rezept.EntwurfFreigeben(Testzeit.Jetzt);
        rezept.EntwurfAnlegen(Testzeit.Jetzt);

        rezept.EntwurfBearbeiten(rezept.Entwurf!.Zutaten, Bezugsform.Ring(24, 5));

        rezept.FassungMitNummer(1)!.Bezugsform.ShouldBeNull();
        rezept.MassgeblicheFassung.Bezugsform.ShouldBeNull();
        Should.Throw<DomainRuleViolationException>(() => rezept.MassgeblicheFassung.UmgerechnetAuf(Bezugsform.Ring(18, 5)))
            .ShouldHaveCode("fassung.ohne_bezugsform");
        rezept.Entwurf!.UmgerechnetAuf(Bezugsform.Ring(12, 5)).Faktor.Wert.ShouldBe(0.25m);
    }

    [Fact]
    public void Verworfener_Entwurf_mit_anderer_Form_laesst_die_geltende_Form_bestehen()
    {
        var rezept = MitForm(Bezugsform.Ring(26, 6), Zeile("Eier", 300));
        rezept.EntwurfFreigeben(Testzeit.Jetzt);
        rezept.EntwurfAnlegen(Testzeit.Jetzt);
        rezept.EntwurfBearbeiten(rezept.Entwurf!.Zutaten, Bezugsform.Blech(60, 40, 2));

        rezept.EntwurfVerwerfen();

        rezept.MassgeblicheFassung.Bezugsform.ShouldBe(Bezugsform.Ring(26, 6));
    }

    [Fact]
    public void Umrechnung_einer_freigegebenen_Fassung_nutzt_deren_Form_nicht_die_des_Entwurfs()
    {
        var rezept = MitForm(Bezugsform.Ring(26, 6), Zeile("Eier", 400));
        rezept.EntwurfFreigeben(Testzeit.Jetzt);
        rezept.EntwurfAnlegen(Testzeit.Jetzt);
        rezept.EntwurfBearbeiten(rezept.Entwurf!.Zutaten, Bezugsform.Ring(13, 6));

        rezept.FassungMitNummer(1)!.UmgerechnetAuf(Bezugsform.Ring(13, 6)).Faktor.Wert.ShouldBe(0.25m);
        rezept.FassungMitNummer(2)!.UmgerechnetAuf(Bezugsform.Ring(13, 6)).Faktor.Wert.ShouldBe(1m);
    }
}
