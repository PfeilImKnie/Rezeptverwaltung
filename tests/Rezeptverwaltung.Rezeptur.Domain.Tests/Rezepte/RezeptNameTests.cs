using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

public sealed class RezeptNameTests
{
    [Fact]
    public void Name_wird_getrimmt() =>
        RezeptName.Von("  Sachertorte ").Wert.ShouldBe("Sachertorte");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Leerer_Name_ist_ungueltig(string? name) =>
        Should.Throw<DomainRuleViolationException>(() => RezeptName.Von(name)).ShouldHaveCode("rezept.name_leer");

    [Fact]
    public void Name_mit_Maximallaenge_ist_gueltig() =>
        RezeptName.Von(new string('a', RezeptName.MaximaleLaenge)).Wert.Length.ShouldBe(RezeptName.MaximaleLaenge);

    [Fact]
    public void Zu_langer_Name_ist_ungueltig() =>
        Should.Throw<DomainRuleViolationException>(() => RezeptName.Von(new string('a', RezeptName.MaximaleLaenge + 1)))
            .ShouldHaveCode("rezept.name_zu_lang");

    [Fact]
    public void Darstellung_ist_der_Name() =>
        RezeptName.Von("Linzer Torte").ToString().ShouldBe("Linzer Torte");
}
