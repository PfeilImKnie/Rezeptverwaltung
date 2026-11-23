using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

public sealed class SkalierungsfaktorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Faktor_muss_positiv_sein(decimal wert) =>
        Should.Throw<DomainRuleViolationException>(() => Skalierungsfaktor.Von(wert)).ShouldHaveCode("skalierung.faktor_ungueltig");

    [Fact]
    public void Zwischen_berechnet_Verhaeltnis_von_Ziel_zu_Ausgang() =>
        Skalierungsfaktor.Zwischen(Gewicht.Erfasst(500), Gewicht.Erfasst(750)).Wert.ShouldBe(1.5m);

    [Fact]
    public void Zwischen_lehnt_Ausgangsgewicht_null_ab() =>
        Should.Throw<DomainRuleViolationException>(() => Skalierungsfaktor.Zwischen(Gewicht.Null, Gewicht.Erfasst(1)))
            .ShouldHaveCode("skalierung.ausgang_null");
}
