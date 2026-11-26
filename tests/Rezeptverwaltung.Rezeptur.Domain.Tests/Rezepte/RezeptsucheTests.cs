using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

public sealed class RezeptsucheTests
{
    private static bool Passt(string suche, string name) => Rezeptsuche.Von(suche).Passt(RezeptName.Von(name));

    [Theory]
    [InlineData("Sacher", "Sachertorte")]
    [InlineData("torte", "Sachertorte")]
    [InlineData("SACHERTORTE", "Sachertorte")]
    [InlineData("kase", "Käsekuchen")]
    [InlineData("Käse", "Kasekuchen")]
    [InlineData("creme", "Crème brûlée")]
    [InlineData("  Linzer  ", "Linzer Torte")]
    [InlineData("er To", "Linzer Torte")]
    public void Name_der_den_Suchtext_enthaelt_passt(string suche, string name) =>
        Passt(suche, name).ShouldBeTrue();

    [Theory]
    [InlineData("Linzer", "Sachertorte")]
    [InlineData("Sachertorten", "Sachertorte")]
    [InlineData("Linzer  Torte", "Linzer Torte")]
    public void Name_ohne_den_Suchtext_passt_nicht(string suche, string name) =>
        Passt(suche, name).ShouldBeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t 　")]
    public void Leere_Suche_findet_alles(string? suche)
    {
        var rezeptsuche = Rezeptsuche.Von(suche);
        rezeptsuche.Text.ShouldBeEmpty();
        rezeptsuche.Passt(RezeptName.Von("Biskuit")).ShouldBeTrue();
    }

    [Fact]
    public void Suchtext_wird_am_Rand_bereinigt() =>
        Rezeptsuche.Von("  Torte \n").Text.ShouldBe("Torte");

    [Fact]
    public void Suchtext_mit_Hoechstlaenge_in_Codepoints_ist_gueltig() =>
        Rezeptsuche.Von(string.Concat(Enumerable.Repeat("🎂", Rezeptsuche.MaximaleLaenge))).Text.ShouldNotBeEmpty();

    [Fact]
    public void Zu_langer_Suchtext_ist_ungueltig() =>
        Should.Throw<DomainRuleViolationException>(() => Rezeptsuche.Von(new string('a', Rezeptsuche.MaximaleLaenge + 1)))
            .ShouldHaveCode("suche.zu_lang");

    [Fact]
    public void Zu_lang_zaehlt_den_Leerraum_am_Rand_mit() =>
        Should.Throw<DomainRuleViolationException>(() => Rezeptsuche.Von(" " + new string('a', Rezeptsuche.MaximaleLaenge)))
            .ShouldHaveCode("suche.zu_lang");

    [Fact]
    public void Zu_lang_ist_eine_ungueltige_Eingabe() =>
        Should.Throw<DomainRuleViolationException>(() => Rezeptsuche.Von(new string('a', 101))).Art.ShouldBe(Regelart.UngueltigeEingabe);

    [Fact]
    public void Reihenfolge_ist_alphabetisch_ohne_Gross_und_Kleinschreibung()
    {
        string[] roh = ["linzer Torte", "Biskuit", "Apfelstrudel", "Zitronentarte", "Äpfelkuchen"];
        var namen = roh.Select(RezeptName.Von);

        namen.Order(Rezeptsuche.Reihenfolge).Select(n => n.Wert)
            .ShouldBe(["Äpfelkuchen", "Apfelstrudel", "Biskuit", "linzer Torte", "Zitronentarte"]); // Umlaut zählt wie der Grundbuchstabe (DIN 5007-1)
    }

    [Fact]
    public void Gleiche_Namen_bis_auf_Schreibweise_sind_eindeutig_sortiert()
    {
        var a = RezeptName.Von("biskuit");
        var b = RezeptName.Von("Biskuit");

        Rezeptsuche.Reihenfolge.Compare(a, b).ShouldNotBe(0);
        Rezeptsuche.Reihenfolge.Compare(a, b).ShouldBe(-Rezeptsuche.Reihenfolge.Compare(b, a));
        Rezeptsuche.Reihenfolge.Compare(a, RezeptName.Von("biskuit")).ShouldBe(0);
    }

    [Fact]
    public void Passt_verlangt_einen_Namen() =>
        Should.Throw<ArgumentNullException>(() => Rezeptsuche.Von(null).Passt(null!));
}
