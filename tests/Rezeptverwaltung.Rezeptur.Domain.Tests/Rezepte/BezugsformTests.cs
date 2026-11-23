using CsCheck;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

/// <summary>Bezugsform und Umrechnen über das Volumen (ADR 0014).</summary>
public sealed class BezugsformTests
{
    private static readonly Gen<decimal> Mass = Gen.Int[10, 2000].Select(zehntel => zehntel / 10m);
    private static readonly Gen<decimal> Hoehe = Gen.Int[1, 500].Select(zehntel => zehntel / 10m);
    private static readonly Gen<Bezugsform> Form = Gen.OneOf(
        Gen.Select(Mass, Hoehe, Bezugsform.Ring),
        Gen.Select(Mass, Mass, Hoehe, Bezugsform.Blech));

    [Fact]
    public void Ring_hat_Durchmesser_und_Hoehe()
    {
        var ring = Bezugsform.Ring(26, 6);

        ring.Art.ShouldBe(Formart.Ring);
        ring.DurchmesserCm.ShouldBe(26m);
        ring.HoeheCm.ShouldBe(6m);
        ring.LaengeCm.ShouldBeNull();
        ring.BreiteCm.ShouldBeNull();
        ring.ToString().ShouldBe("Ring Ø 26 × 6 cm");
    }

    [Fact]
    public void Blech_hat_Laenge_Breite_und_Hoehe()
    {
        var blech = Bezugsform.Blech(60, 40, 1.5m);

        blech.Art.ShouldBe(Formart.Blech);
        blech.DurchmesserCm.ShouldBeNull();
        blech.VolumenCm3.ShouldBe(3600m);
        blech.ToString().ShouldBe("Blech 60 × 40 × 1,5 cm");
    }

    [Fact]
    public void Volumen_des_Rings_ist_Kreisflaeche_mal_Hoehe() =>
        Math.Abs(Bezugsform.Ring(26, 6).VolumenCm3 - 3185.5750m).ShouldBeLessThan(0.0001m);

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    [InlineData(18.5)]
    public void Mass_an_den_Grenzen_ist_gueltig(decimal cm) =>
        Bezugsform.Blech(cm, cm, 5).LaengeCm.ShouldBe(cm);

    [Theory]
    [InlineData(0.9)]
    [InlineData(200.1)]
    [InlineData(0)]
    [InlineData(-10)]
    public void Mass_ausserhalb_des_Bereichs_ist_ungueltig(decimal cm)
    {
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Ring(cm, 5)).ShouldHaveCode("form.mass_ungueltig");
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Blech(cm, 30, 5)).ShouldHaveCode("form.mass_ungueltig");
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Blech(30, cm, 5)).ShouldHaveCode("form.mass_ungueltig");
    }

    [Fact]
    public void Meldung_nennt_das_ungueltige_Mass()
    {
        // Wird in der Backstube angezeigt bzw. von Alexa vorgelesen: Es muss klar sein, welches Maß falsch ist.
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Ring(500, 5)).Message.ShouldContain("Durchmesser");
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Blech(500, 30, 5)).Message.ShouldContain("Länge");
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Blech(30, 500, 5)).Message.ShouldContain("Breite");
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Ring(20, 60)).Message.ShouldContain("Höhe");
    }

    [Fact]
    public void Meldung_zu_genauer_Angaben_nennt_das_Mass()
    {
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Ring(18.05m, 6)).Message.ShouldContain("Durchmesser");
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Blech(60.05m, 40, 2)).Message.ShouldContain("Länge");
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Blech(60, 40.55m, 2)).Message.ShouldContain("Breite");
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Blech(60, 40, 2.05m)).Message.ShouldContain("Höhe");
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(50)]
    public void Hoehe_an_den_Grenzen_ist_gueltig(decimal cm) =>
        Bezugsform.Ring(20, cm).HoeheCm.ShouldBe(cm);

    [Theory]
    [InlineData(0)]
    [InlineData(50.1)]
    [InlineData(-1)]
    public void Hoehe_ausserhalb_des_Bereichs_ist_ungueltig(decimal cm) =>
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Ring(20, cm)).ShouldHaveCode("form.hoehe_ungueltig");

    [Theory]
    [InlineData(18.05, 6)]
    [InlineData(18, 6.01)]
    public void Masse_genauer_als_ein_Millimeter_sind_ungueltig(decimal durchmesser, decimal hoehe) =>
        Should.Throw<DomainRuleViolationException>(() => Bezugsform.Ring(durchmesser, hoehe)).ShouldHaveCode("form.zu_genau");

    [Fact]
    public void Ring_26_auf_18_bei_gleicher_Hoehe_ergibt_Flaechenverhaeltnis() =>
        Math.Abs(Bezugsform.Ring(26, 6).FaktorFuer(Bezugsform.Ring(18, 6)).Wert - (18m * 18m / (26m * 26m))).ShouldBeLessThan(0.000_000_01m);

    [Fact]
    public void Hoehere_Form_braucht_entsprechend_mehr_Masse() =>
        Math.Abs(Bezugsform.Ring(26, 6).FaktorFuer(Bezugsform.Ring(26, 8)).Wert - (8m / 6m)).ShouldBeLessThan(0.000_000_01m);

    [Fact]
    public void Ring_laesst_sich_auf_Blech_umrechnen() =>
        Math.Abs(Bezugsform.Blech(60, 40, 1).FaktorFuer(Bezugsform.Ring(26, 1)).Wert - 0.2212205m).ShouldBeLessThan(0.000_001m);

    [Fact]
    public void Zielform_null_ist_ein_Programmierfehler() =>
        Should.Throw<ArgumentNullException>(() => Bezugsform.Ring(26, 6).FaktorFuer(null!));

    [Fact]
    public void Eigenschaft_Umrechnen_auf_dieselbe_Form_ergibt_Faktor_1() =>
        Form.Sample(form => form.FaktorFuer(form).Wert.ShouldBe(1m));

    [Fact]
    public void Eigenschaft_Hin_und_zurueck_ergibt_Faktor_1() =>
        Gen.Select(Form, Form).Sample((a, b) =>
            Math.Abs((a.FaktorFuer(b).Wert * b.FaktorFuer(a).Wert) - 1m).ShouldBeLessThan(0.000_000_001m));

    [Fact]
    public void Eigenschaft_Umrechnungen_lassen_sich_verketten() =>
        Gen.Select(Form, Form, Form).Sample((a, b, c) =>
        {
            var direkt = a.FaktorFuer(c).Wert;
            var ueberB = a.FaktorFuer(b).Wert * b.FaktorFuer(c).Wert;
            Math.Abs(direkt - ueberB).ShouldBeLessThan(direkt * 0.000_000_001m);
        });
}
