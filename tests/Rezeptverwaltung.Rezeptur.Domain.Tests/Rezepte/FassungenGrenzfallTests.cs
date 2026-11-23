using CsCheck;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

/// <summary>
/// Unabhängiger Test (pr-test) zu Fassungen: Grenzfälle und Eigenschaften, abgeleitet aus den Akzeptanzkriterien.
/// </summary>
public sealed class FassungenGrenzfallTests
{
    private static readonly DateTimeOffset T1 = Testzeit.Jetzt;
    private static readonly DateTimeOffset T2 = Testzeit.Jetzt.AddHours(1);
    private static readonly DateTimeOffset T3 = Testzeit.Jetzt.AddHours(2);
    private static readonly DateTimeOffset T4 = Testzeit.Jetzt.AddHours(3);

    private static Zutatenzeile Zeile(string bezeichnung, decimal gramm) => Zutatenzeile.Von(bezeichnung, Gewicht.Erfasst(gramm));

    private static Rezept Sachermasse()
    {
        var rezept = Rezept.Anlegen(
            RezeptName.Von("Sachermasse"),
            [Zeile("Butter", 140), Zeile("Zucker", 70), Zeile("Eier", 280), Zeile("Zucker", 70), Zeile("Mehl", 140)],
            T1);
        rezept.EntwurfFreigeben(T2);
        return rezept;
    }

    [Fact]
    public void Neuer_Entwurf_kopiert_die_neueste_freigegebene_Fassung_nicht_die_erste()
    {
        var rezept = Sachermasse();
        rezept.EntwurfAnlegen(T3);
        rezept.EntwurfBearbeiten([Zeile("Butter", 150), Zeile("Mehl", 150)]);
        rezept.EntwurfFreigeben(T3);

        var entwurf = rezept.EntwurfAnlegen(T4);

        entwurf.Nummer.ShouldBe(3);
        entwurf.Zutaten.ShouldBe([Zeile("Butter", 150), Zeile("Mehl", 150)]);
    }

    [Fact]
    public void Kopie_erhaelt_doppelte_Zutaten_und_Reihenfolge()
    {
        var rezept = Sachermasse();

        var entwurf = rezept.EntwurfAnlegen(T3);

        entwurf.Zutaten.Select(z => z.Bezeichnung).ShouldBe(["Butter", "Zucker", "Eier", "Zucker", "Mehl"]);
        entwurf.Gesamtgewicht.ShouldBe(Gewicht.Erfasst(700));
    }

    [Fact]
    public void Erstellungszeitpunkt_bleibt_bei_Bearbeitung_und_Freigabe_erhalten()
    {
        var rezept = Rezept.Anlegen(RezeptName.Von("Linzer Teig"), [Zeile("Mehl", 300)], T1);

        rezept.EntwurfBearbeiten([Zeile("Mehl", 280), Zeile("Butter", 200)]);
        var fassung = rezept.EntwurfFreigeben(T2);

        fassung.ErstelltAm.ShouldBe(T1);
        fassung.FreigegebenAm.ShouldBe(T2);
    }

    [Fact]
    public void Spaetere_Freigabe_aendert_die_Zeitpunkte_frueherer_Fassungen_nicht()
    {
        var rezept = Sachermasse();
        rezept.EntwurfAnlegen(T3);
        rezept.EntwurfFreigeben(T4);

        var fassung1 = rezept.FassungMitNummer(1)!;
        var fassung2 = rezept.FassungMitNummer(2)!;
        (fassung1.ErstelltAm, fassung1.FreigegebenAm).ShouldBe((T1, T2));
        (fassung2.ErstelltAm, fassung2.FreigegebenAm).ShouldBe((T3, T4));
    }

    [Fact]
    public void Entwurf_auf_leere_Liste_bearbeitet_kann_nicht_freigegeben_werden_und_aktuelle_Fassung_bleibt()
    {
        var rezept = Sachermasse();
        rezept.EntwurfAnlegen(T3);
        rezept.EntwurfBearbeiten([]);
        var ereignisseVorher = rezept.DomainEvents.Count;

        var verletzung = Should.Throw<DomainRuleViolationException>(() => rezept.EntwurfFreigeben(T4));

        verletzung.ShouldHaveCode("fassung.ohne_zutaten");
        verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
        var entwurf = rezept.Entwurf.ShouldNotBeNull();
        entwurf.Status.ShouldBe(FassungsStatus.Entwurf);
        entwurf.FreigegebenAm.ShouldBeNull();
        rezept.AktuelleFassung!.Nummer.ShouldBe(1);
        rezept.DomainEvents.Count.ShouldBe(ereignisseVorher);
    }

    [Fact]
    public void Entwurf_mit_einer_einzigen_Zutat_von_0_01_g_kann_freigegeben_werden()
    {
        var rezept = Rezept.Anlegen(RezeptName.Von("Prise"), [Zeile("Salz", 0.01m)], T1);

        rezept.EntwurfFreigeben(T2).Status.ShouldBe(FassungsStatus.Freigegeben);
    }

    [Fact]
    public void Verwerfen_macht_die_aktuelle_Fassung_wieder_zur_einzigen_und_aendert_sie_nicht()
    {
        var rezept = Sachermasse();
        var vorher = rezept.Zutaten.ToList();
        rezept.EntwurfAnlegen(T3);
        rezept.EntwurfBearbeiten([Zeile("Kakao", 1)]);

        rezept.EntwurfVerwerfen();

        rezept.MassgeblicheFassung.Nummer.ShouldBe(1);
        rezept.Zutaten.ShouldBe(vorher);
        rezept.LetzteFassungsnummer.ShouldBe(2);
    }

    [Fact]
    public void Faktor_Skalierung_ohne_Angabe_nutzt_die_freigegebene_Fassung_nicht_den_Entwurf()
    {
        var rezept = Sachermasse();
        rezept.EntwurfAnlegen(T3);
        rezept.EntwurfBearbeiten([Zeile("Butter", 1000)]);

        var skaliert = rezept.Skaliert(Skalierungsfaktor.Von(2));

        skaliert.Select(z => z.Menge.Gramm).ShouldBe([280m, 140m, 560m, 140m, 280m]);
    }

    [Fact]
    public void Entwurf_ohne_Zutaten_ist_als_Fassung_nicht_skalierbar_die_aktuelle_aber_schon()
    {
        var rezept = Sachermasse();
        var entwurf = rezept.EntwurfAnlegen(T3);
        rezept.EntwurfBearbeiten([]);

        Should.Throw<DomainRuleViolationException>(() => entwurf.SkaliertAuf(Gewicht.Erfasst(100))).ShouldHaveCode("rezept.ohne_zutaten");
        rezept.SkaliertAuf(Gewicht.Erfasst(350)).Sum(z => z.Menge.Gramm).ShouldBe(350m);
    }

    // --- Eigenschaften

    [Fact]
    public void Eigenschaft_Jeder_neue_Entwurf_ist_eine_exakte_Kopie_der_aktuellen_Fassung()
    {
        Gen.Select(Generatoren.Zutatenzeile.List[1, 20], Generatoren.Zutatenzeile.List[1, 20]).Sample((erste, zweite) =>
        {
            var rezept = Rezept.Anlegen(RezeptName.Von("Probe"), erste, T1);
            rezept.EntwurfFreigeben(T1);
            rezept.EntwurfAnlegen(T2).Zutaten.ShouldBe(erste);
            rezept.EntwurfBearbeiten(zweite);
            rezept.EntwurfFreigeben(T2);

            rezept.EntwurfAnlegen(T3).Zutaten.ShouldBe(zweite);
            rezept.FassungMitNummer(1)!.Zutaten.ShouldBe(erste);
        });
    }

    [Fact]
    public void Eigenschaft_Bearbeitung_des_Entwurfs_wirkt_nie_auf_Abfragen_ohne_Fassungsangabe()
    {
        Gen.Select(Generatoren.RezeptMitZutaten, Generatoren.Zutatenzeile.List[0, 20], Generatoren.ErfasstesGewicht.Where(g => !g.IstNull))
            .Sample((rezept, neu, ziel) =>
            {
                rezept.EntwurfFreigeben(T1);
                var zutatenVorher = rezept.Zutaten.ToList();
                var skaliertVorher = rezept.SkaliertAuf(ziel).ToList();

                rezept.EntwurfAnlegen(T2);
                rezept.EntwurfBearbeiten(neu);

                rezept.Zutaten.ShouldBe(zutatenVorher);
                rezept.SkaliertAuf(ziel).ShouldBe(skaliertVorher);
                rezept.Entwurf!.Zutaten.ShouldBe(neu);
            });
    }
}
