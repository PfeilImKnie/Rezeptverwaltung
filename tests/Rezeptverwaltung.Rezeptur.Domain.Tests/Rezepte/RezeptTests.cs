using CsCheck;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

public sealed class RezeptTests
{
    private static Rezept Muerbeteig() => Rezept.Anlegen(
        RezeptName.Von("Mürbeteig 1-2-3"),
        [
            Zutatenzeile.Von("Zucker", Gewicht.Erfasst(100)),
            Zutatenzeile.Von("Butter", Gewicht.Erfasst(200)),
            Zutatenzeile.Von("Mehl", Gewicht.Erfasst(300)),
        ], Testzeit.Jetzt);

    [Fact]
    public void Anlegen_vergibt_Id_und_meldet_RezeptAngelegt()
    {
        var rezept = Muerbeteig();

        rezept.Id.Wert.ShouldNotBe(Guid.Empty);
        rezept.DomainEvents.ShouldHaveSingleItem().ShouldBe(new RezeptAngelegt(rezept.Id));
    }

    [Fact]
    public void Anlegen_uebernimmt_Name_und_Zutaten_in_Reihenfolge()
    {
        var rezept = Muerbeteig();

        rezept.Name.Wert.ShouldBe("Mürbeteig 1-2-3");
        rezept.Zutaten.Select(z => z.Bezeichnung).ShouldBe(["Zucker", "Butter", "Mehl"]);
    }

    [Fact]
    public void Dieselbe_Zutat_darf_mehrfach_vorkommen()
    {
        var rezept = Rezept.Anlegen(
            RezeptName.Von("Biskuit"),
            [Zutatenzeile.Von("Zucker", Gewicht.Erfasst(50)), Zutatenzeile.Von("Zucker", Gewicht.Erfasst(75))], Testzeit.Jetzt);

        rezept.Zutaten.Count.ShouldBe(2);
    }

    [Fact]
    public void DomainEvents_lassen_sich_leeren()
    {
        var rezept = Muerbeteig();

        rezept.DomainEventsLeeren();

        rezept.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Anlegen_ohne_Name_oder_Zutatenliste_ist_ein_Programmierfehler()
    {
        Should.Throw<ArgumentNullException>(() => Rezept.Anlegen(null!, [], Testzeit.Jetzt));
        Should.Throw<ArgumentNullException>(() => Rezept.Anlegen(RezeptName.Von("X"), null!, Testzeit.Jetzt));
    }

    [Fact]
    public void ZutatHinzufuegen_ohne_Zeile_ist_ein_Programmierfehler() =>
        Should.Throw<ArgumentNullException>(() => Muerbeteig().ZutatHinzufuegen(null!));

    [Fact]
    public void Gesamtgewicht_ist_Summe_aller_Zutaten() =>
        Muerbeteig().Gesamtgewicht.ShouldBe(Gewicht.Erfasst(600));

    [Fact]
    public void Rezept_ohne_Zutaten_wiegt_nichts() =>
        Rezept.Anlegen(RezeptName.Von("Leer"), [], Testzeit.Jetzt).Gesamtgewicht.ShouldBe(Gewicht.Null);

    [Fact]
    public void SkaliertAuf_rechnet_Mengen_proportional_um()
    {
        var skaliert = Muerbeteig().SkaliertAuf(Gewicht.Erfasst(900));

        skaliert.Select(z => z.Menge.Gramm).ShouldBe([150m, 300m, 450m]);
    }

    [Fact]
    public void SkaliertAuf_veraendert_das_Rezept_nicht()
    {
        var rezept = Muerbeteig();

        _ = rezept.SkaliertAuf(Gewicht.Erfasst(1200));

        rezept.Gesamtgewicht.ShouldBe(Gewicht.Erfasst(600));
    }

    [Fact]
    public void SkaliertAuf_ohne_Zutaten_ist_ein_Zustandskonflikt()
    {
        var verletzung = Should.Throw<DomainRuleViolationException>(
            () => Rezept.Anlegen(RezeptName.Von("Leer"), [], Testzeit.Jetzt).SkaliertAuf(Gewicht.Erfasst(100)));

        verletzung.ShouldHaveCode("rezept.ohne_zutaten");
        verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
    }

    [Fact]
    public void SkaliertAuf_null_Gramm_ist_eine_ungueltige_Eingabe()
    {
        var verletzung = Should.Throw<DomainRuleViolationException>(() => Muerbeteig().SkaliertAuf(Gewicht.Null));

        verletzung.ShouldHaveCode("skalierung.ziel_null");
        verletzung.Art.ShouldBe(Regelart.UngueltigeEingabe);
    }

    // --- Eigenschaften, die für JEDES gültige Rezept gelten müssen ---

    [Fact]
    public void Eigenschaft_Skaliertes_Rezept_wiegt_das_Zielgewicht() =>
        Gen.Select(Generatoren.RezeptMitZutaten, Generatoren.ErfasstesGewicht).Sample((rezept, ziel) =>
        {
            var summe = rezept.SkaliertAuf(ziel).Sum(z => z.Menge.Gramm);

            // decimal rechnet mit 28 Stellen; Rundungsfehler liegen weit unter 1 µg.
            Math.Abs(summe - ziel.Gramm).ShouldBeLessThan(0.000_001m);
        });

    [Fact]
    public void Eigenschaft_Skalieren_erhaelt_die_Verhaeltnisse_der_Zutaten() =>
        Gen.Select(Generatoren.RezeptMitZutaten, Generatoren.ErfasstesGewicht).Sample((rezept, ziel) =>
        {
            var skaliert = rezept.SkaliertAuf(ziel);

            for (var i = 0; i < rezept.Zutaten.Count; i++)
            {
                var anteilVorher = rezept.Zutaten[i].Menge.Gramm / rezept.Gesamtgewicht.Gramm;
                var anteilNachher = skaliert[i].Menge.Gramm / ziel.Gramm;
                Math.Abs(anteilVorher - anteilNachher).ShouldBeLessThan(0.000_000_1m);
            }
        });

    [Fact]
    public void Eigenschaft_Skalieren_erhaelt_Bezeichnungen_und_Reihenfolge() =>
        Gen.Select(Generatoren.RezeptMitZutaten, Generatoren.ErfasstesGewicht).Sample((rezept, ziel) =>
            rezept.SkaliertAuf(ziel).Select(z => z.Bezeichnung).ShouldBe(rezept.Zutaten.Select(z => z.Bezeichnung)));

    [Fact]
    public void Eigenschaft_Skalieren_auf_eigenes_Gewicht_aendert_nichts() =>
        Generatoren.RezeptMitZutaten.Sample(rezept =>
            rezept.SkaliertAuf(rezept.Gesamtgewicht).Select(z => z.Menge).ShouldBe(rezept.Zutaten.Select(z => z.Menge)));
}
