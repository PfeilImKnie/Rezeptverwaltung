using CsCheck;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

/// <summary>
/// Die Regelart bestimmt den HTTP-Status: ungültige Eingabe → 422, Zustandskonflikt → 409.
/// Ein Zustandskonflikt setzt eine gültige Anfrage voraus (api/openapi.yaml).
/// </summary>
public sealed class RegelartTests
{
    private static Rezept Leer() => Rezept.Anlegen(RezeptName.Von("Noch leer"), [], Testzeit.Jetzt);

    [Fact]
    public void Ungueltiges_Zielgewicht_hat_Vorrang_vor_fehlenden_Zutaten()
    {
        var verletzung = Should.Throw<DomainRuleViolationException>(() => Leer().SkaliertAuf(Gewicht.Null));

        verletzung.ShouldHaveCode("skalierung.ziel_null");
        verletzung.Art.ShouldBe(Regelart.UngueltigeEingabe);
    }

    [Fact]
    public void Eigenschaft_Rezept_ohne_Zutaten_ist_fuer_jedes_gueltige_Zielgewicht_ein_Zustandskonflikt() =>
        Generatoren.ErfasstesGewicht.Sample(ziel =>
        {
            var verletzung = Should.Throw<DomainRuleViolationException>(() => Leer().SkaliertAuf(ziel));

            verletzung.ShouldHaveCode("rezept.ohne_zutaten");
            verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
        });

    [Fact]
    public void Rezept_ohne_Zutaten_bleibt_nach_verweigerter_Skalierung_unveraendert()
    {
        var rezept = Leer();

        _ = Should.Throw<DomainRuleViolationException>(() => rezept.SkaliertAuf(Gewicht.Erfasst(100)));

        rezept.Zutaten.ShouldBeEmpty();
        rezept.Gesamtgewicht.ShouldBe(Gewicht.Null);
    }

    [Fact]
    public void Nach_Hinzufuegen_einer_Zutat_ist_Skalieren_wieder_moeglich()
    {
        var rezept = Leer();
        rezept.ZutatHinzufuegen(Zutatenzeile.Von("Butter", Gewicht.Erfasst(250)));

        rezept.SkaliertAuf(Gewicht.Erfasst(500)).ShouldHaveSingleItem().Menge.ShouldBe(Gewicht.Erfasst(500));
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(-1)]
    [InlineData(1_000_000_000.01)]
    public void Ungueltig_erfasstes_Gewicht_ist_eine_ungueltige_Eingabe(double gramm) =>
        Should.Throw<DomainRuleViolationException>(() => Gewicht.Erfasst((decimal)gramm)).Art.ShouldBe(Regelart.UngueltigeEingabe);

    [Fact]
    public void Leere_Zutat_und_leerer_Name_sind_ungueltige_Eingaben()
    {
        Should.Throw<DomainRuleViolationException>(() => Zutatenzeile.Von(" ", Gewicht.Erfasst(1))).Art.ShouldBe(Regelart.UngueltigeEingabe);
        Should.Throw<DomainRuleViolationException>(() => Zutatenzeile.Von("Mehl", Gewicht.Null)).Art.ShouldBe(Regelart.UngueltigeEingabe);
        Should.Throw<DomainRuleViolationException>(() => RezeptName.Von(" ")).Art.ShouldBe(Regelart.UngueltigeEingabe);
    }

    // --- Shared Kernel: Fabrikmethoden und Konstruktoren ---

    [Fact]
    public void ZustandWenn_wirft_einen_Zustandskonflikt_mit_Code_und_Meldung()
    {
        var verletzung = Should.Throw<DomainRuleViolationException>(
            () => DomainRuleViolationException.ZustandWenn(true, "test.zustand", "Zustand lässt das nicht zu."));

        verletzung.ShouldHaveCode("test.zustand");
        verletzung.Message.ShouldBe("Zustand lässt das nicht zu.");
        verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
    }

    [Fact]
    public void ZustandWenn_und_Wenn_werfen_nicht_wenn_die_Regel_eingehalten_ist()
    {
        Should.NotThrow(() => DomainRuleViolationException.ZustandWenn(false, "test.zustand", "x"));
        Should.NotThrow(() => DomainRuleViolationException.Wenn(false, "test.eingabe", "x"));
    }

    [Fact]
    public void Wenn_wirft_eine_ungueltige_Eingabe() =>
        Should.Throw<DomainRuleViolationException>(() => DomainRuleViolationException.Wenn(true, "test.eingabe", "Falsch."))
            .Art.ShouldBe(Regelart.UngueltigeEingabe);

    [Fact]
    public void Konstruktoren_ohne_Regelart_bedeuten_ungueltige_Eingabe()
    {
        new DomainRuleViolationException("a.b", "Meldung").Art.ShouldBe(Regelart.UngueltigeEingabe);
        new DomainRuleViolationException().Art.ShouldBe(Regelart.UngueltigeEingabe);
        new DomainRuleViolationException("Meldung").Art.ShouldBe(Regelart.UngueltigeEingabe);
        new DomainRuleViolationException("Meldung", new InvalidOperationException()).Art.ShouldBe(Regelart.UngueltigeEingabe);
    }

    [Fact]
    public void Konstruktor_mit_Regelart_uebernimmt_alle_Angaben()
    {
        var verletzung = new DomainRuleViolationException("a.b", "Meldung", Regelart.Zustandskonflikt);

        verletzung.Code.ShouldBe("a.b");
        verletzung.Message.ShouldBe("Meldung");
        verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
    }
}
