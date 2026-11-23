using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

public sealed class GewichtTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(0.01)]
    [InlineData(250)]
    [InlineData(1_000_000_000)]
    public void Erfasst_akzeptiert_gueltige_Gewichte(decimal gramm) =>
        Gewicht.Erfasst(gramm).Gramm.ShouldBe(gramm);

    [Fact]
    public void Erfasst_lehnt_negatives_Gewicht_ab() =>
        Should.Throw<DomainRuleViolationException>(() => Gewicht.Erfasst(-0.01m)).ShouldHaveCode("gewicht.negativ");

    [Fact]
    public void Erfasst_lehnt_Gewicht_ueber_der_Obergrenze_ab() =>
        Should.Throw<DomainRuleViolationException>(() => Gewicht.Erfasst(1_000_000_000.01m)).ShouldHaveCode("gewicht.zu_gross");

    [Theory]
    [InlineData(0.001)]
    [InlineData(12.345)]
    public void Erfasst_lehnt_mehr_als_zwei_Nachkommastellen_ab(decimal gramm) =>
        Should.Throw<DomainRuleViolationException>(() => Gewicht.Erfasst(gramm)).ShouldHaveCode("gewicht.zu_genau");

    [Fact]
    public void Erfasst_akzeptiert_nachgestellte_Nullen() =>
        Gewicht.Erfasst(12.3400m).Gramm.ShouldBe(12.34m);

    [Fact]
    public void Addition_summiert_Gramm() =>
        (Gewicht.Erfasst(100) + Gewicht.Erfasst(50.5m)).ShouldBe(Gewicht.Erfasst(150.5m));

    [Fact]
    public void Skalieren_behaelt_volle_Genauigkeit() =>
        Gewicht.Erfasst(10).Skaliert(Skalierungsfaktor.Von(0.3333m)).Gramm.ShouldBe(3.333m);

    [Fact]
    public void Null_ist_null() =>
        Gewicht.Null.IstNull.ShouldBeTrue();

    [Fact]
    public void Positives_Gewicht_ist_nicht_null() =>
        Gewicht.Erfasst(0.01m).IstNull.ShouldBeFalse();

    [Fact]
    public void Vergleichsoperatoren_ordnen_nach_Gramm()
    {
        var leicht = Gewicht.Erfasst(1);
        var schwer = Gewicht.Erfasst(2);

        (leicht < schwer).ShouldBeTrue();
        (schwer > leicht).ShouldBeTrue();
        (leicht <= Gewicht.Erfasst(1)).ShouldBeTrue();
        (schwer >= Gewicht.Erfasst(2)).ShouldBeTrue();
        (schwer < leicht).ShouldBeFalse();
        (leicht > schwer).ShouldBeFalse();
        (schwer <= leicht).ShouldBeFalse();
        (leicht >= schwer).ShouldBeFalse();
    }

    [Fact]
    public void Gleiche_Gewichte_sind_weder_kleiner_noch_groesser()
    {
        var a = Gewicht.Erfasst(5);
        var b = Gewicht.Erfasst(5);

        (a < b).ShouldBeFalse();
        (a > b).ShouldBeFalse();
        (a <= b).ShouldBeTrue();
        (a >= b).ShouldBeTrue();
    }

    [Fact]
    public void Darstellung_in_deutscher_Schreibweise() =>
        Gewicht.Erfasst(1234.5m).ToString().ShouldBe("1234,5 g");
}
