using Rezeptverwaltung.Web.Client.Api;
using Rezeptverwaltung.Web.Client.Komponenten;

namespace Rezeptverwaltung.Web.Tests;

public sealed class EingabenTests
{
    private static ZeileEingabe Zeile(string bezeichnung, string gramm) => new() { Bezeichnung = bezeichnung, Gramm = gramm };

    [Fact]
    public void Gueltige_Eingaben_werden_zum_Inhalt()
    {
        var fehler = new List<string>();

        var inhalt = Inhaltseingabe.Lesen(
            new FormEingabe { Art = Form.Ring, Durchmesser = "26", Hoehe = "6,5" },
            [Zeile(" Butter ", "140"), Zeile("", ""), Zeile("Zucker", "110,25")],
            fehler);

        fehler.ShouldBeEmpty();
        inhalt.Zutaten.ShouldBe([new Zutat("Butter", 140m), new Zutat("Zucker", 110.25m)]);
        inhalt.Bezugsform.ShouldBe(Form.AlsRing(26m, 6.5m));
    }

    [Fact]
    public void Fehler_nennen_die_Zeile()
    {
        var fehler = new List<string>();

        Inhaltseingabe.Lesen(new FormEingabe(), [Zeile("Butter", "1.000"), Zeile("", "5"), Zeile("Mehl", "")], fehler);

        fehler.ShouldBe([
            "Zeile 1: Bitte ohne Tausenderpunkt eingeben, z. B. 1000.",
            "Zeile 2: Bitte eine Zutat eintragen.",
            "Zeile 3: Bitte eine Zahl eingeben.",
        ]);
    }

    [Fact]
    public void Ohne_Formart_gibt_es_keine_Form()
    {
        var fehler = new List<string>();

        new FormEingabe { Art = string.Empty, Durchmesser = "xx" }.Lesen(fehler).ShouldBeNull();
        fehler.ShouldBeEmpty();
    }

    [Fact]
    public void Blech_braucht_Laenge_Breite_und_Hoehe()
    {
        var fehler = new List<string>();

        new FormEingabe { Art = Form.Blech, Laenge = "40", Breite = "", Hoehe = "2,55" }.Lesen(fehler).ShouldBeNull();

        fehler.ShouldBe(["Höhe: Höchstens 1 Stelle nach dem Komma.", "Breite: Bitte eine Zahl eingeben."]);
        new FormEingabe { Art = Form.Blech, Laenge = "40", Breite = "30", Hoehe = "2" }.Lesen(fehler = [])
            .ShouldBe(Form.AlsBlech(40, 30, 2));
    }

    [Fact]
    public void Mehr_als_100_Zutaten_sind_zu_viele()
    {
        var fehler = new List<string>();
        var zeilen = Enumerable.Range(1, Inhaltseingabe.MaximaleZeilen + 1).Select(i => Zeile($"Zutat {i}", "1")).ToList();

        Inhaltseingabe.Lesen(new FormEingabe(), zeilen, fehler);

        fehler.ShouldBe(["Höchstens 100 Zutaten je Rezept."]);
        Inhaltseingabe.Lesen(new FormEingabe(), zeilen[..100], fehler = []);
        fehler.ShouldBeEmpty();
    }

    [Fact]
    public void Vorhandene_Form_und_Zutaten_lassen_sich_wieder_bearbeiten()
    {
        var ring = FormEingabe.Aus(Form.AlsRing(26, 6.5m));
        ring.Art.ShouldBe(Form.Ring);
        ring.Durchmesser.ShouldBe("26");
        ring.Hoehe.ShouldBe("6,5");

        var blech = FormEingabe.Aus(Form.AlsBlech(40, 30.5m, 2));
        (blech.Laenge, blech.Breite, blech.Hoehe).ShouldBe(("40", "30,5", "2"));

        FormEingabe.Aus(null).Art.ShouldBeEmpty();
        var zeile = ZeileEingabe.Aus(new Zutat("Butter", 1234.5m));
        (zeile.Bezeichnung, zeile.Gramm).ShouldBe(("Butter", "1234,5"));
    }

    [Fact]
    public void Beschreibung_der_Form()
    {
        Form.AlsRing(26, 6.5m).Beschreibung.ShouldBe("Ring Ø 26 cm, 6,5 cm hoch");
        Form.AlsBlech(40, 30, 2).Beschreibung.ShouldBe("Blech 40 × 30 cm, 2 cm hoch");
    }
}
