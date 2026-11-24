using CsCheck;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

/// <summary>Fassungen mit Entwurf und Freigabe (ADR 0013).</summary>
public sealed class FassungenTests
{
    private static readonly DateTimeOffset Spaeter = Testzeit.Jetzt.AddDays(3);

    private static Zutatenzeile Zeile(string bezeichnung, decimal gramm) => Zutatenzeile.Von(bezeichnung, Gewicht.Erfasst(gramm));

    private static Rezept NeuesRezept() =>
        Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Schoko-Biskuit"), [Zeile("Eier", 300), Zeile("Zucker", 150)], Testzeit.Jetzt);

    private static Rezept FreigegebenesRezept()
    {
        var rezept = NeuesRezept();
        rezept.EntwurfFreigeben(Testzeit.Jetzt);
        return rezept;
    }

    [Fact]
    public void Neues_Rezept_beginnt_mit_Fassung_1_als_Entwurf()
    {
        var rezept = NeuesRezept();

        var fassung = rezept.Fassungen.ShouldHaveSingleItem();
        fassung.Nummer.ShouldBe(1);
        fassung.Status.ShouldBe(FassungsStatus.Entwurf);
        fassung.ErstelltAm.ShouldBe(Testzeit.Jetzt);
        fassung.FreigegebenAm.ShouldBeNull();
        rezept.Entwurf.ShouldBe(fassung);
        rezept.AktuelleFassung.ShouldBeNull();
        rezept.MassgeblicheFassung.ShouldBe(fassung);
        rezept.LetzteFassungsnummer.ShouldBe(1);
    }

    [Fact]
    public void Freigabe_macht_den_Entwurf_zur_aktuellen_Fassung()
    {
        var rezept = NeuesRezept();

        var fassung = rezept.EntwurfFreigeben(Spaeter);

        fassung.Status.ShouldBe(FassungsStatus.Freigegeben);
        fassung.FreigegebenAm.ShouldBe(Spaeter);
        rezept.AktuelleFassung.ShouldBe(fassung);
        rezept.Entwurf.ShouldBeNull();
        rezept.DomainEvents.ShouldContain(new FassungFreigegeben(rezept.Id, 1));
    }

    [Fact]
    public void Fassung_ohne_Zutaten_kann_nicht_freigegeben_werden()
    {
        var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Leer"), [], Testzeit.Jetzt);

        var verletzung = Should.Throw<DomainRuleViolationException>(() => rezept.EntwurfFreigeben(Spaeter));

        verletzung.ShouldHaveCode("fassung.ohne_zutaten");
        verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
        rezept.Entwurf.ShouldNotBeNull();
    }

    [Fact]
    public void Neuer_Entwurf_ist_eine_Kopie_der_aktuellen_Fassung_mit_naechster_Nummer()
    {
        var rezept = FreigegebenesRezept();

        var entwurf = rezept.EntwurfAnlegen(Spaeter);

        entwurf.Nummer.ShouldBe(2);
        entwurf.Status.ShouldBe(FassungsStatus.Entwurf);
        entwurf.ErstelltAm.ShouldBe(Spaeter);
        entwurf.Zutaten.ShouldBe(rezept.FassungMitNummer(1)!.Zutaten);
        rezept.Entwurf.ShouldBe(entwurf);
    }

    [Fact]
    public void Waehrend_ein_Entwurf_existiert_bleibt_die_freigegebene_Fassung_massgeblich()
    {
        var rezept = FreigegebenesRezept();

        rezept.EntwurfAnlegen(Spaeter);
        rezept.EntwurfBearbeiten([Zeile("Eier", 320)]);

        rezept.MassgeblicheFassung.Nummer.ShouldBe(1);
        rezept.Zutaten.ShouldBe(rezept.FassungMitNummer(1)!.Zutaten);
        rezept.Gesamtgewicht.ShouldBe(Gewicht.Erfasst(450));
    }

    [Fact]
    public void Bearbeiten_des_Entwurfs_veraendert_die_freigegebene_Fassung_nicht()
    {
        var rezept = FreigegebenesRezept();
        var vorher = rezept.FassungMitNummer(1)!.Zutaten.ToList();

        rezept.EntwurfAnlegen(Spaeter);
        rezept.EntwurfBearbeiten([Zeile("Eier", 320), Zeile("Kakao", 40)]);

        rezept.FassungMitNummer(1)!.Zutaten.ShouldBe(vorher);
        rezept.FassungMitNummer(2)!.Zutaten.Select(z => z.Bezeichnung).ShouldBe(["Eier", "Kakao"]);
    }

    [Fact]
    public void Es_gibt_hoechstens_einen_Entwurf()
    {
        var rezept = FreigegebenesRezept();
        rezept.EntwurfAnlegen(Spaeter);

        var verletzung = Should.Throw<DomainRuleViolationException>(() => rezept.EntwurfAnlegen(Spaeter));

        verletzung.ShouldHaveCode("rezept.entwurf_vorhanden");
        verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
    }

    [Fact]
    public void Ohne_Entwurf_kann_nichts_bearbeitet_freigegeben_oder_verworfen_werden()
    {
        var rezept = FreigegebenesRezept();

        Should.Throw<DomainRuleViolationException>(() => rezept.EntwurfBearbeiten([])).ShouldHaveCode("rezept.kein_entwurf");
        Should.Throw<DomainRuleViolationException>(() => rezept.EntwurfFreigeben(Spaeter)).ShouldHaveCode("rezept.kein_entwurf");
        Should.Throw<DomainRuleViolationException>(rezept.EntwurfVerwerfen).ShouldHaveCode("rezept.kein_entwurf");
        Should.Throw<DomainRuleViolationException>(() => rezept.ZutatHinzufuegen(Zeile("Salz", 1))).ShouldHaveCode("rezept.kein_entwurf");
    }

    [Fact]
    public void Verwerfen_entfernt_den_Entwurf()
    {
        var rezept = FreigegebenesRezept();
        rezept.EntwurfAnlegen(Spaeter);

        rezept.EntwurfVerwerfen();

        rezept.Entwurf.ShouldBeNull();
        rezept.Fassungen.ShouldHaveSingleItem().Nummer.ShouldBe(1);
    }

    [Fact]
    public void Der_einzige_Entwurf_kann_nicht_verworfen_werden() =>
        Should.Throw<DomainRuleViolationException>(NeuesRezept().EntwurfVerwerfen).ShouldHaveCode("rezept.einzige_fassung");

    [Fact]
    public void Nummer_eines_verworfenen_Entwurfs_wird_nie_wiederverwendet()
    {
        var rezept = FreigegebenesRezept();
        rezept.EntwurfAnlegen(Spaeter);
        rezept.EntwurfVerwerfen();

        var neu = rezept.EntwurfAnlegen(Spaeter);

        neu.Nummer.ShouldBe(3);
        rezept.LetzteFassungsnummer.ShouldBe(3);
    }

    [Fact]
    public void Unbekannte_Fassungsnummer_ergibt_null() =>
        NeuesRezept().FassungMitNummer(2).ShouldBeNull();

    [Fact]
    public void Fassung_ist_einzeln_skalierbar()
    {
        var rezept = FreigegebenesRezept();
        rezept.EntwurfAnlegen(Spaeter);
        rezept.EntwurfBearbeiten([Zeile("Eier", 200), Zeile("Zucker", 200)]);

        rezept.FassungMitNummer(2)!.SkaliertAuf(Gewicht.Erfasst(800)).Select(z => z.Menge.Gramm).ShouldBe([400m, 400m]);
        rezept.SkaliertAuf(Gewicht.Erfasst(900)).Select(z => z.Menge.Gramm).ShouldBe([600m, 300m]);
    }

    [Fact]
    public void Zutatenliste_mit_null_Eintrag_ist_ein_Programmierfehler()
    {
        var rezept = NeuesRezept();

        Should.Throw<ArgumentNullException>(() => rezept.EntwurfBearbeiten([Zeile("Eier", 1), null!]));
        Should.Throw<ArgumentNullException>(() => rezept.EntwurfBearbeiten(null!));
        rezept.Zutaten.Count.ShouldBe(2);
    }

    // --- Eigenschaft: Egal in welcher Reihenfolge gearbeitet wird, gelten die Grundregeln der Fassungen.

    private enum Operation
    {
        EntwurfAnlegen,
        EntwurfBearbeiten,
        EntwurfFreigeben,
        EntwurfVerwerfen,
    }

    [Fact]
    public void Eigenschaft_Freigegebene_Fassungen_aendern_sich_nie_und_Nummern_sind_eindeutig()
    {
        var operationen = Gen.Select(Gen.Enum<Operation>(), Generatoren.Zutatenzeile.List[0, 4]).List[1, 40];

        operationen.Sample(folge =>
        {
            var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Probe"), [Zeile("Mehl", 100)], Testzeit.Jetzt);
            var freigegeben = new Dictionary<int, List<Zutatenzeile>>();
            var vergeben = new HashSet<int> { 1 };
            var schritt = 0;

            foreach (var (operation, zutaten) in folge)
            {
                var zeit = Testzeit.Jetzt.AddMinutes(++schritt);
                try
                {
                    switch (operation)
                    {
                        case Operation.EntwurfAnlegen:
                            vergeben.Add(rezept.EntwurfAnlegen(zeit).Nummer).ShouldBeTrue("Fassungsnummer wurde wiederverwendet");
                            break;
                        case Operation.EntwurfBearbeiten:
                            rezept.EntwurfBearbeiten(zutaten);
                            break;
                        case Operation.EntwurfFreigeben:
                            var fassung = rezept.EntwurfFreigeben(zeit);
                            freigegeben.Add(fassung.Nummer, fassung.Zutaten.ToList());
                            break;
                        case Operation.EntwurfVerwerfen:
                            rezept.EntwurfVerwerfen();
                            break;
                    }
                }
                catch (DomainRuleViolationException verletzung)
                {
                    verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
                }

                rezept.Fassungen.Count(f => f.IstEntwurf).ShouldBeLessThanOrEqualTo(1);
                rezept.Fassungen.Select(f => f.Nummer).ShouldBeUnique();
                rezept.Fassungen.ShouldNotBeEmpty();
                foreach (var (nummer, stand) in freigegeben)
                {
                    var fassung = rezept.FassungMitNummer(nummer).ShouldNotBeNull();
                    fassung.Status.ShouldBe(FassungsStatus.Freigegeben);
                    fassung.Zutaten.ShouldBe(stand);
                }

                rezept.MassgeblicheFassung.ShouldBe(rezept.AktuelleFassung ?? rezept.Entwurf);
                if (freigegeben.Count > 0)
                {
                    rezept.AktuelleFassung!.Nummer.ShouldBe(freigegeben.Keys.Max());
                }
            }
        });
    }
}
