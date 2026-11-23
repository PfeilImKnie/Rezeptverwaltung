using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

public sealed class ZutatenzeileTests
{
    [Fact]
    public void Bezeichnung_wird_getrimmt() =>
        Zutatenzeile.Von(" Butter ", Gewicht.Erfasst(250)).Bezeichnung.ShouldBe("Butter");

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Leere_Bezeichnung_ist_ungueltig(string? bezeichnung) =>
        Should.Throw<DomainRuleViolationException>(() => Zutatenzeile.Von(bezeichnung, Gewicht.Erfasst(1)))
            .ShouldHaveCode("zutat.bezeichnung_leer");

    [Fact]
    public void Bezeichnung_mit_Maximallaenge_ist_gueltig() =>
        Zutatenzeile.Von(new string('x', Zutatenzeile.MaximaleBezeichnungsLaenge), Gewicht.Erfasst(1))
            .Bezeichnung.Length.ShouldBe(Zutatenzeile.MaximaleBezeichnungsLaenge);

    [Fact]
    public void Zu_lange_Bezeichnung_ist_ungueltig() =>
        Should.Throw<DomainRuleViolationException>(
                () => Zutatenzeile.Von(new string('x', Zutatenzeile.MaximaleBezeichnungsLaenge + 1), Gewicht.Erfasst(1)))
            .ShouldHaveCode("zutat.bezeichnung_zu_lang");

    [Fact]
    public void Menge_null_ist_ungueltig() =>
        Should.Throw<DomainRuleViolationException>(() => Zutatenzeile.Von("Salz", Gewicht.Null)).ShouldHaveCode("zutat.menge_null");

    [Fact]
    public void Skalieren_aendert_nur_die_Menge()
    {
        var zeile = Zutatenzeile.Von("Zucker", Gewicht.Erfasst(200));

        var skaliert = zeile.Skaliert(Skalierungsfaktor.Von(0.5m));

        skaliert.Bezeichnung.ShouldBe("Zucker");
        skaliert.Menge.ShouldBe(Gewicht.Erfasst(100));
    }
}
