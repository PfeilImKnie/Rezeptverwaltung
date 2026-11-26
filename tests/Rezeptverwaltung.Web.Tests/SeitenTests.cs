using AngleSharp.Dom;
using Bunit;
using Rezeptverwaltung.Web.Client.Api;
using Rezeptverwaltung.Web.Client.Pages;

namespace Rezeptverwaltung.Web.Tests;

/// <summary>Die Seiten der Weboberfläche mit einer API-Attrappe: Anzeige, Eingaben, Rechte je Rolle.</summary>
public sealed class RezeptlisteSeiteTests : SeitenTestBasis
{
    [Fact]
    public void Zeigt_die_Rezepte_mit_Hinweis_auf_offene_Entwuerfe()
    {
        Api.Hinzufuegen("Sachertorte", Sachertorte, freigegeben: true);
        Api.Hinzufuegen("Biskuit", Sachertorte);
        Api.Hinzufuegen("Linzer Torte", Sachertorte, freigegeben: true, mitEntwurf: true);

        var seite = Seite<Rezepte>(Mitarbeiter);

        var eintraege = seite.FindAll(".rezeptliste li");
        eintraege.Select(e => e.QuerySelector(".name")!.TextContent).ShouldBe(["Biskuit", "Linzer Torte", "Sachertorte"]);
        eintraege[0].TextContent.ShouldContain("noch nicht freigegeben");
        eintraege[1].TextContent.ShouldContain("Entwurf offen");
        eintraege[2].QuerySelector(".marke-entwurf").ShouldBeNull();
    }

    [Fact]
    public void Suche_fragt_die_API_beim_Tippen()
    {
        Api.Hinzufuegen("Sachertorte", Sachertorte);
        Api.Hinzufuegen("Biskuit", Sachertorte);
        var seite = Seite<Rezepte>(Mitarbeiter);

        seite.Find("input.suche").Input("sacher");

        seite.WaitForAssertion(() => seite.FindAll(".rezeptliste .name").Select(e => e.TextContent).ShouldBe(["Sachertorte"]));
        Api.Aufrufe.ShouldContain("Auflisten:sacher");
    }

    [Fact]
    public void Leere_Liste_und_leeres_Suchergebnis_werden_erklaert()
    {
        var seite = Seite<Rezepte>(Mitarbeiter);
        seite.Find(".hinweis").TextContent.ShouldBe("Noch keine Rezepte.");

        seite.Find("input.suche").Input("Mohn");

        seite.WaitForAssertion(() => seite.Find(".hinweis").TextContent.ShouldBe("Kein Rezept passt zur Suche."));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Neues_Rezept_nur_mit_Recht_zum_Bearbeiten(bool bearbeiten, bool sichtbar)
    {
        var seite = Seite<Rezepte>(new Berechtigungen(true, bearbeiten, false));

        seite.FindAll("a[href='rezepte/neu']").Count.ShouldBe(sichtbar ? 1 : 0);
    }

    [Fact]
    public void Fehler_beim_Laden_wird_angezeigt()
    {
        Api.NaechsterFehler = new ApiFehlerException(503, null, "Server nicht erreichbar.");

        var seite = Seite<Rezepte>(Mitarbeiter);

        seite.Find("[role=alert]").TextContent.ShouldContain("Server nicht erreichbar.");
    }
}

public sealed class NeuesRezeptSeiteTests : SeitenTestBasis
{
    private static void Zeile(IRenderedComponent<RezeptNeu> seite, int index, string zutat, string gramm)
    {
        seite.FindAll("input.bezeichnung")[index].Input(zutat);
        seite.FindAll(".gramm input")[index].Input(gramm);
    }

    [Fact]
    public void Legt_ein_Rezept_mit_deutschen_Zahlen_an_und_zeigt_es()
    {
        var seite = Seite<RezeptNeu>(Konditor);
        seite.Find("label.name input").Change("Sachertorte");
        seite.FindAll(".form-editor button").Single(b => b.TextContent == "Ring").Click();
        seite.Find("input[placeholder='26']").Input("26");
        seite.Find("input[placeholder='6']").Input("6,5");
        Zeile(seite, 0, "Butter", "140,5");
        Zeile(seite, 2, "Zucker", "110");

        seite.Find("form").Submit();

        Api.Aufrufe.ShouldContain("Anlegen:Sachertorte");
        Api.LetzterInhalt!.Zutaten.ShouldBe([new Zutat("Butter", 140.5m), new Zutat("Zucker", 110m)]);
        Api.LetzterInhalt.Bezugsform.ShouldBe(Form.AlsRing(26, 6.5m));
        Navigation.Uri.ShouldStartWith("http://localhost/rezepte/");
    }

    [Fact]
    public void Ungueltige_Eingaben_werden_vor_dem_Senden_gemeldet()
    {
        var seite = Seite<RezeptNeu>(Konditor);
        Zeile(seite, 0, "Butter", "1.000");

        seite.Find("form").Submit();

        var meldung = seite.Find("[role=alert]").TextContent;
        meldung.ShouldContain("Bitte einen Namen eintragen.");
        meldung.ShouldContain("Zeile 1: Bitte ohne Tausenderpunkt eingeben, z. B. 1000.");
        Api.Aufrufe.ShouldBeEmpty();
    }

    [Fact]
    public void Fachlicher_Fehler_der_API_wird_angezeigt()
    {
        var seite = Seite<RezeptNeu>(Konditor);
        seite.Find("label.name input").Change("Torte");
        Zeile(seite, 0, "Butter", "5");
        Api.NaechsterFehler = new ApiFehlerException(422, "form.zu_gross", "Der Durchmesser darf höchstens 200 cm sein.");

        seite.Find("form").Submit();

        seite.Find("[role=alert]").TextContent.ShouldContain("Der Durchmesser darf höchstens 200 cm sein.");
        Navigation.Uri.ShouldBe("http://localhost/");
    }

    [Fact]
    public void Zeilen_hinzufuegen_verschieben_und_entfernen()
    {
        var seite = Seite<RezeptNeu>(Konditor);
        Zeile(seite, 0, "Butter", "1");
        Zeile(seite, 1, "Zucker", "2");

        seite.Find("button[aria-label='Zeile 2 nach oben']").Click();
        seite.FindAll("input.bezeichnung").Select(e => e.GetAttribute("value")).Take(2).ShouldBe(["Zucker", "Butter"]);

        seite.Find("button[aria-label='Zeile 1 entfernen']").Click();
        seite.FindAll("input.bezeichnung").Count.ShouldBe(2);

        seite.FindAll(".zutaten-editor > button").Single().Click();
        seite.FindAll("input.bezeichnung").Count.ShouldBe(3);
        seite.Find("button[aria-label='Zeile 1 nach oben']").HasAttribute("disabled").ShouldBeTrue();
        seite.Find("button[aria-label='Zeile 3 nach unten']").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void Ohne_Recht_kein_Formular()
    {
        var seite = Seite<RezeptNeu>(Mitarbeiter);

        seite.FindAll("form").ShouldBeEmpty();
        seite.Find(".hinweis").TextContent.ShouldContain("fehlt dir das Recht");
    }
}

public sealed class RezeptSeiteTests : SeitenTestBasis
{
    [Fact]
    public void Zeigt_Zutaten_Gesamtgewicht_Form_und_Fassungen()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte, freigegeben: true, mitEntwurf: true);

        var seite = Seite<Rezept>(Mitarbeiter, p => p.Add(s => s.Id, id));

        seite.Find("h1").TextContent.ShouldBe("Sachertorte");
        seite.Find(".fassung-info").TextContent.ShouldContain("Fassung 1, freigegeben");
        seite.Find(".fassung-info").TextContent.ShouldContain("Ring Ø 26 cm, 6 cm hoch");
        seite.FindAll("table.zutaten tbody tr").Select(z => z.TextContent).ShouldBe(["Butter140 g", "Zucker110 g", "Eier300 g"]);
        seite.Find("table.zutaten tfoot").TextContent.ShouldContain("550 g");
        seite.FindAll(".fassungen li").Select(f => f.TextContent).ShouldBe(["Fassung 2Entwurf vom 24.11.2026", "Fassung 1freigegeben am 24.11.2026"]);
    }

    [Fact]
    public void Mitarbeiter_sieht_den_Entwurf_kann_ihn_aber_nicht_bearbeiten()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte, freigegeben: true, mitEntwurf: true);

        var seite = Seite<Rezept>(Mitarbeiter, p => p.Add(s => s.Id, id));

        seite.Find(".aktionen").TextContent.Trim().ShouldBe("Entwurf ansehen");
        seite.FindAll($"a[href='rezepte/{id}/entwurf']").ShouldBeEmpty();
    }

    [Fact]
    public void Konditor_bearbeitet_den_offenen_Entwurf()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte);

        var seite = Seite<Rezept>(Konditor, p => p.Add(s => s.Id, id));

        seite.Find($"a[href='rezepte/{id}/entwurf']").TextContent.ShouldBe("Entwurf (Fassung 1) bearbeiten");
        seite.Find(".fassung-info").TextContent.ShouldContain("noch nicht freigegeben");
    }

    [Fact]
    public void Neuer_Entwurf_wird_mit_dem_Stand_angelegt_und_geoeffnet()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte, freigegeben: true);
        var stand = Api.Stand(id);
        var seite = Seite<Rezept>(Konditor, p => p.Add(s => s.Id, id));

        seite.FindAll("button").Single(b => b.TextContent == "Neuen Entwurf anlegen").Click();

        Api.LetzterStand.ShouldBe(stand);
        Navigation.Uri.ShouldBe($"http://localhost/rezepte/{id}/entwurf");
    }

    [Fact]
    public void Ohne_Recht_kein_neuer_Entwurf()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte, freigegeben: true);

        var seite = Seite<Rezept>(Mitarbeiter, p => p.Add(s => s.Id, id));

        seite.Find(".aktionen").TextContent.Trim().ShouldBeEmpty();
    }

    [Fact]
    public void Unbekanntes_Rezept()
    {
        var seite = Seite<Rezept>(Mitarbeiter, p => p.Add(s => s.Id, Guid.NewGuid()));

        seite.Find("h1").TextContent.ShouldBe("Rezept nicht gefunden");
    }

    [Fact]
    public void Ohne_Zutaten_gibt_es_nichts_umzurechnen()
    {
        var id = Api.Hinzufuegen("Leer", new RezeptInhalt([], null));

        var seite = Seite<Rezept>(Mitarbeiter, p => p.Add(s => s.Id, id));

        seite.FindAll(".umrechnen").ShouldBeEmpty();
    }
}

public sealed class UmrechnenTests : SeitenTestBasis
{
    private static string Normalisiert(string text) =>
        string.Join(' ', text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Nach_Gewicht()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte);
        var seite = Seite<Rezept>(Mitarbeiter, p => p.Add(s => s.Id, id));

        seite.Find(".umrechnen input").Change("1100");
        seite.Find(".umrechnen form").Submit();

        Api.LetzteUmrechnung.ShouldBe(new Umrechnung.AufGewicht(1100));
        Api.LetzteUmrechnungFassung.ShouldBeNull();
        Normalisiert(seite.Find(".ergebnis p").TextContent).ShouldBe("Faktor 2");
        seite.FindAll(".ergebnis tbody tr").Select(z => z.TextContent).ShouldBe(["Butter280 g", "Zucker220 g", "Eier600 g"]);
    }

    [Fact]
    public void Auf_einen_anderen_Ring_mit_der_eigenen_Form_vorbelegt()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte);
        var seite = Seite<Rezept>(Mitarbeiter, p => p.Add(s => s.Id, id));
        seite.FindAll(".umrechnen .auswahl button").Single(b => b.TextContent == "Ring").Click();

        seite.FindAll(".umrechnen form input").Select(f => f.GetAttribute("value")).ShouldBe(["26", "6"]);
        seite.FindAll(".umrechnen form input")[0].Change("18");
        seite.Find(".umrechnen form").Submit();

        Api.LetzteUmrechnung.ShouldBe(new Umrechnung.AufForm(Form.AlsRing(18, 6)));
        Normalisiert(seite.Find(".ergebnis p").TextContent).ShouldBe("Für Ring Ø 18 cm, 6 cm hoch: Faktor 0,4793");
    }

    [Fact]
    public void Auf_ein_Blech()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte);
        var seite = Seite<Rezept>(Mitarbeiter, p => p.Add(s => s.Id, id));
        seite.FindAll(".umrechnen .auswahl button").Single(b => b.TextContent == "Blech").Click();

        seite.FindAll(".umrechnen form input")[0].Change("40");
        seite.FindAll(".umrechnen form input")[1].Change("30");
        seite.FindAll(".umrechnen form input")[2].Change("2,5");
        seite.Find(".umrechnen form").Submit();

        Api.LetzteUmrechnung.ShouldBe(new Umrechnung.AufForm(Form.AlsBlech(40, 30, 2.5m)));
    }

    [Fact]
    public void Ohne_Bezugsform_nur_nach_Gewicht()
    {
        var id = Api.Hinzufuegen("Vanillecreme", new RezeptInhalt([new Zutat("Milch", 500)], null));

        var seite = Seite<Rezept>(Mitarbeiter, p => p.Add(s => s.Id, id));

        seite.FindAll(".umrechnen .auswahl button").Select(b => b.TextContent).ShouldBe(["Gewicht"]);
        seite.Find(".umrechnen .hinweis").TextContent.ShouldContain("keine Bezugsform");
    }

    [Fact]
    public void Ungueltige_Eingabe_wird_gemeldet_ohne_API_Aufruf()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte);
        var seite = Seite<Rezept>(Mitarbeiter, p => p.Add(s => s.Id, id));

        seite.Find(".umrechnen input").Change("abc");
        seite.Find(".umrechnen form").Submit();

        seite.Find(".umrechnen [role=alert]").TextContent.ShouldContain("Gesamtgewicht: Bitte nur Ziffern");
        Api.Aufrufe.ShouldNotContain("Umrechnen");
    }

    [Fact]
    public void Fehler_der_API_beim_Umrechnen()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte);
        var seite = Seite<Rezept>(Mitarbeiter, p => p.Add(s => s.Id, id));
        Api.NaechsterFehler = new ApiFehlerException(422, "form.zu_klein", "Der Durchmesser muss mindestens 1 cm sein.");

        seite.Find(".umrechnen input").Change("10");
        seite.Find(".umrechnen form").Submit();

        seite.Find(".umrechnen [role=alert]").TextContent.ShouldContain("mindestens 1 cm");
        seite.FindAll(".ergebnis").ShouldBeEmpty();
    }

    [Fact]
    public void Eine_bestimmte_Fassung_umrechnen()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte, freigegeben: true, mitEntwurf: true);
        var seite = Seite<Fassung>(Mitarbeiter, p => p.Add(s => s.Id, id).Add(s => s.Nummer, 2));

        seite.Find("h1").TextContent.ShouldBe("Fassung 2");
        seite.Find(".umrechnen input").Change("550");
        seite.Find(".umrechnen form").Submit();

        Api.LetzteUmrechnungFassung.ShouldBe(2);
    }
}

public sealed class EntwurfSeiteTests : SeitenTestBasis
{
    private Guid _id;

    private IRenderedComponent<Entwurf> Oeffnen(Berechtigungen rechte)
    {
        _id = Api.Hinzufuegen("Sachertorte", Sachertorte, freigegeben: true, mitEntwurf: true);
        return Seite<Entwurf>(rechte, p => p.Add(s => s.Id, _id));
    }

    private static IElement Knopf(IRenderedComponent<Entwurf> seite, string text) =>
        seite.FindAll("button").Single(b => b.TextContent.Trim() == text);

    [Fact]
    public void Laedt_den_Entwurf_zum_Bearbeiten()
    {
        var seite = Oeffnen(Konditor);

        seite.Find("h1").TextContent.ShouldBe("Sachertorte – Entwurf (Fassung 2)");
        seite.FindAll("input.bezeichnung").Select(e => e.GetAttribute("value")).ShouldBe(["Butter", "Zucker", "Eier"]);
        seite.FindAll(".gramm input").Select(e => e.GetAttribute("value")).ShouldBe(["140", "110", "300"]);
        seite.Find(".form-editor button.aktiv").TextContent.ShouldBe("Ring");
        Knopf(seite, "Speichern").HasAttribute("disabled").ShouldBeTrue(); // noch nichts geändert
    }

    [Fact]
    public void Speichern_schickt_den_Stand_und_uebernimmt_den_neuen()
    {
        var seite = Oeffnen(Konditor);
        var stand = Api.Stand(_id);

        seite.FindAll(".gramm input")[0].Input("150");
        seite.Find(".hinweis").TextContent.ShouldBe("Ungespeicherte Änderungen.");
        seite.Find("form").Submit();

        Api.LetzterStand.ShouldBe(stand);
        Api.LetzterInhalt!.Zutaten[0].ShouldBe(new Zutat("Butter", 150));
        Api.LetzterInhalt.Bezugsform.ShouldBe(Form.AlsRing(26, 6));
        Knopf(seite, "Speichern").HasAttribute("disabled").ShouldBeTrue();

        seite.FindAll(".gramm input")[1].Input("120");
        seite.Find("form").Submit();
        Api.LetzterStand.ShouldBe(Api.Stand(_id).Replace("3", "2", StringComparison.Ordinal)); // der Stand nach dem ersten Speichern
    }

    [Fact]
    public void Form_entfernen_speichert_ohne_Form()
    {
        var seite = Oeffnen(Konditor);

        seite.FindAll(".form-editor button").Single(b => b.TextContent == "Ohne Form").Click();
        seite.Find("form").Submit();

        Api.LetzterInhalt!.Bezugsform.ShouldBeNull();
    }

    [Fact]
    public void Freigeben_nur_als_Inhaber_und_nicht_mit_ungespeicherten_Aenderungen()
    {
        Oeffnen(Konditor).FindAll("button").ShouldNotContain(b => b.TextContent == "Freigeben");

        var seite = Seite<Entwurf>(Inhaber, p => p.Add(s => s.Id, _id));
        Knopf(seite, "Freigeben").HasAttribute("disabled").ShouldBeFalse();

        seite.FindAll(".gramm input")[0].Input("150");
        Knopf(seite, "Freigeben").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void Freigeben_fuehrt_zurueck_zum_Rezept()
    {
        var seite = Oeffnen(Inhaber);
        var stand = Api.Stand(_id);

        Knopf(seite, "Freigeben").Click();

        Api.Aufrufe.ShouldContain("Freigeben");
        Api.LetzterStand.ShouldBe(stand);
        Navigation.Uri.ShouldBe($"http://localhost/rezepte/{_id}");
    }

    [Fact]
    public void Verwerfen_fragt_nach()
    {
        var seite = Oeffnen(Konditor);

        Knopf(seite, "Verwerfen").Click();
        Api.Aufrufe.ShouldNotContain("Verwerfen");
        Knopf(seite, "Nein").Click();
        Knopf(seite, "Verwerfen").Click();
        Knopf(seite, "Ja, verwerfen").Click();

        Api.Aufrufe.ShouldContain("Verwerfen");
        Navigation.Uri.ShouldBe($"http://localhost/rezepte/{_id}");
    }

    [Fact]
    public void Veralteter_Stand_bietet_Neu_laden_an()
    {
        var seite = Oeffnen(Konditor);
        seite.FindAll(".gramm input")[0].Input("150");
        Api.NaechsterFehler = new ApiFehlerException(412, "rezept.veraltet", "Das Rezept wurde inzwischen an anderer Stelle geändert. Bitte neu laden.");

        seite.Find("form").Submit();
        seite.Find("[role=alert]").TextContent.ShouldContain("inzwischen an anderer Stelle geändert");
        Knopf(seite, "Neu laden").Click();

        seite.FindAll("[role=alert]").ShouldBeEmpty();
        seite.FindAll(".gramm input")[0].GetAttribute("value").ShouldBe("140");
    }

    [Fact]
    public async Task Inzwischen_anderswo_freigegebener_Entwurf_verschwindet_nach_Neu_laden()
    {
        var seite = Oeffnen(Konditor);
        seite.FindAll(".gramm input")[0].Input("150");
        await Api.FreigebenAsync(_id, Api.Stand(_id), Xunit.TestContext.Current.CancellationToken); // an einem anderen Gerät
        Api.NaechsterFehler = new ApiFehlerException(412, "rezept.veraltet", "Das Rezept wurde inzwischen an anderer Stelle geändert. Bitte neu laden.");
        seite.Find("form").Submit();

        Knopf(seite, "Neu laden").Click();

        seite.Find("h1").TextContent.ShouldBe("Kein Entwurf");
        seite.FindAll("form").ShouldBeEmpty();
        seite.Find($"a[href='rezepte/{_id}']").ShouldNotBeNull();
    }

    [Fact]
    public void Ungueltige_Eingabe_wird_nicht_gespeichert()
    {
        var seite = Oeffnen(Konditor);

        seite.FindAll(".gramm input")[0].Input("1,234");
        seite.Find("form").Submit();

        seite.Find("[role=alert]").TextContent.ShouldContain("Zeile 1: Höchstens 2 Stellen nach dem Komma.");
        Api.Aufrufe.ShouldNotContain("EntwurfSpeichern");
    }

    [Fact]
    public void Ohne_Entwurf()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte, freigegeben: true);

        var seite = Seite<Entwurf>(Konditor, p => p.Add(s => s.Id, id));

        seite.Find("h1").TextContent.ShouldBe("Kein Entwurf");
    }

    [Fact]
    public void Mitarbeiter_kann_nicht_bearbeiten()
    {
        var seite = Oeffnen(Mitarbeiter);

        seite.FindAll("form").ShouldBeEmpty();
        seite.Find(".hinweis").TextContent.ShouldContain("fehlt dir das Recht");
    }
}
