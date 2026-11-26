using System.Net;
using System.Text;
using Bunit;
using CsCheck;
using Microsoft.Extensions.DependencyInjection;
using Rezeptverwaltung.Web.Client;
using Rezeptverwaltung.Web.Client.Api;
using Rezeptverwaltung.Web.Client.Pages;

namespace Rezeptverwaltung.Web.Tests;

/// <summary>
/// Unabhängiger Test der Zahlen in der Oberfläche (Anforderung 1): Eine angezeigte Zahl wird nie still als eine
/// andere Zahl zurückgelesen („1.000“ ist nicht 1 g), Eingaben überstehen den Hin- und Rückweg.
/// </summary>
public sealed class ZahlenEigenschaftenTests
{
    /// <summary>Erfasste Gewichte: 0 bis 1.000.000.000 g auf 0,01 g.</summary>
    private static readonly Gen<decimal> Gramm = Gen.Long[0, 100_000_000_000].Select(hundertstel => hundertstel / 100m);

    /// <summary>Maße: 0,1 bis 200 cm auf 0,1 cm.</summary>
    private static readonly Gen<decimal> Zentimeter = Gen.Int[1, 2000].Select(zehntel => zehntel / 10m);

    [Fact]
    public void Eingabe_und_Lesen_ergeben_wieder_dasselbe_Gewicht() =>
        Gramm.Sample(gramm => Zahlen.Lesen(Zahlen.Eingabe(gramm), 2, out _) == gramm);

    [Fact]
    public void Eingabe_und_Lesen_ergeben_wieder_dasselbe_Mass() =>
        Zentimeter.Sample(cm => Zahlen.Lesen(Zahlen.Eingabe(cm), 1, out _) == cm);

    [Fact]
    public void Angezeigtes_Gewicht_wird_nie_als_andere_Zahl_gelesen() =>
        Gramm.Sample(gramm =>
        {
            var gelesen = Zahlen.Lesen(Zahlen.Anzeigen(gramm), 2, out _);
            return gelesen is null || gelesen == gramm;
        });

    [Fact]
    public void Angezeigtes_Mass_wird_nie_als_andere_Zahl_gelesen() =>
        Gen.Int[1, 100_000].Select(zehntel => zehntel / 10m)
            .Sample(cm =>
            {
                var gelesen = Zahlen.Lesen(Zahlen.Anzeigen(cm), 1, out _);
                return gelesen is null || gelesen == cm;
            });

    [Theory]
    [InlineData("1.000", 1)]
    [InlineData("1.000", 0)]
    [InlineData("10.000", 2)]
    [InlineData("1.000.000", 2)]
    public void Tausenderpunkte_werden_in_jedem_Feld_abgewiesen(string text, int nachkommastellen)
    {
        Zahlen.Lesen(text, nachkommastellen, out var fehler).ShouldBeNull();
        fehler.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Anzeige_ist_deutsch_auch_bei_englischer_Oberflaechensprache()
    {
        var vorher = System.Globalization.CultureInfo.CurrentUICulture;
        var vorherKultur = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("en-GB");
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");

            Zahlen.Anzeigen(1234567.89m).ShouldBe("1.234.567,89");
            Zahlen.Eingabe(0.5m).ShouldBe("0,5");
            Zahlen.Lesen("0,5", 2, out _).ShouldBe(0.5m);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = vorher;
            System.Globalization.CultureInfo.CurrentCulture = vorherKultur;
        }
    }

    [Fact]
    public void Kleinste_erfasste_Menge_wird_nicht_als_0_angezeigt() =>
        Zahlen.Anzeigen(0.01m).ShouldBe("0,01");
}

/// <summary>Unabhängiger Test der Seiten mit Fakes: Rechte, Zahleneingabe, Tausenderpunkt.</summary>
public sealed class UnabhaengigeSeitenTests : SeitenTestBasis
{
    [Fact]
    public void Tausenderpunkt_beim_Umrechnen_nach_Gewicht_wird_abgewiesen_statt_1_g_zu_rechnen()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte);
        var seite = Seite<Rezept>(Mitarbeiter, p => p.Add(s => s.Id, id));

        seite.Find(".umrechnen input").Change("1.000");
        seite.Find(".umrechnen form").Submit();

        Api.Aufrufe.ShouldNotContain("Umrechnen");
        seite.Find(".umrechnen [role=alert]").TextContent.ShouldContain("Gesamtgewicht");
    }

    [Fact]
    public void Tausenderpunkt_im_Ring_wird_abgewiesen()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte);
        var seite = Seite<Rezept>(Mitarbeiter, p => p.Add(s => s.Id, id));
        seite.FindAll(".umrechnen .auswahl button").Single(b => b.TextContent == "Ring").Click();

        seite.FindAll(".umrechnen form input")[0].Change("1.000");
        seite.Find(".umrechnen form").Submit();

        Api.Aufrufe.ShouldNotContain("Umrechnen");
        seite.Find(".umrechnen [role=alert]").TextContent.ShouldContain("Durchmesser");
    }

    [Fact]
    public void Komma_Eingabe_kommt_genau_bei_der_API_an()
    {
        var seite = Seite<RezeptNeu>(Konditor);
        seite.Find("label.name input").Change("Vanillekipferl");
        seite.FindAll("input.bezeichnung")[0].Input("Vanillezucker");
        seite.FindAll(".gramm input")[0].Input("0,01");
        seite.FindAll("input.bezeichnung")[1].Input("Zucker");
        seite.FindAll(".gramm input")[1].Input("1000000000");

        seite.Find("form").Submit();

        Api.LetzterInhalt!.Zutaten.ShouldBe([new Zutat("Vanillezucker", 0.01m), new Zutat("Zucker", 1_000_000_000m)]);
    }

    [Fact]
    public void Doppelte_Zutaten_bleiben_getrennte_Zeilen()
    {
        var seite = Seite<RezeptNeu>(Konditor);
        seite.Find("label.name input").Change("Biskuit");
        seite.FindAll("input.bezeichnung")[0].Input("Zucker");
        seite.FindAll(".gramm input")[0].Input("100");
        seite.FindAll("input.bezeichnung")[1].Input("Eier");
        seite.FindAll(".gramm input")[1].Input("300");
        seite.FindAll("input.bezeichnung")[2].Input("Zucker");
        seite.FindAll(".gramm input")[2].Input("50");

        seite.Find("form").Submit();

        Api.LetzterInhalt!.Zutaten.ShouldBe([new Zutat("Zucker", 100), new Zutat("Eier", 300), new Zutat("Zucker", 50)]);
    }

    [Fact]
    public void Konditor_kann_verwerfen_aber_nicht_freigeben()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte, freigegeben: true, mitEntwurf: true);

        var seite = Seite<Entwurf>(Konditor, p => p.Add(s => s.Id, id));

        var knoepfe = seite.FindAll("button").Select(b => b.TextContent.Trim()).ToList();
        knoepfe.ShouldContain("Verwerfen");
        knoepfe.ShouldContain("Speichern");
        knoepfe.ShouldNotContain("Freigeben");
    }

    [Fact]
    public void Mitarbeiter_sieht_auf_dem_Entwurf_keine_aendernden_Knoepfe()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte, freigegeben: true, mitEntwurf: true);

        var seite = Seite<Entwurf>(Mitarbeiter, p => p.Add(s => s.Id, id));

        seite.FindAll("button").Select(b => b.TextContent.Trim())
            .ShouldNotContain(t => t == "Speichern" || t == "Freigeben" || t == "Verwerfen");
    }

    [Fact]
    public void Mitarbeiter_kann_eine_freigegebene_Fassung_ansehen_und_umrechnen()
    {
        var id = Api.Hinzufuegen("Sachertorte", Sachertorte, freigegeben: true, mitEntwurf: true);

        var seite = Seite<Fassung>(Mitarbeiter, p => p.Add(s => s.Id, id).Add(s => s.Nummer, 1));

        seite.Find(".fassung-info").TextContent.ShouldContain("Freigegeben am 24.11.2026");
        seite.Find(".umrechnen input").Change("275");
        seite.Find(".umrechnen form").Submit();
        Api.LetzteUmrechnungFassung.ShouldBe(1);
        seite.FindAll(".ergebnis tbody tr").Select(z => z.TextContent).ShouldBe(["Butter70 g", "Zucker55 g", "Eier150 g"]);
    }
}

/// <summary>
/// Unabhängiger Test: wackliges WLAN in der Backstube. <see cref="IRezeptApi"/> verspricht, Fehler als
/// <see cref="ApiFehlerException"/> zu liefern, damit die Seiten eine Meldung zeigen statt abzustürzen.
/// </summary>
public sealed class NetzwerkfehlerTests : BunitContext
{
    private readonly Netz _netz = new();

    public NetzwerkfehlerTests()
    {
        Services.AddSingleton(new HttpClient(new CsrfHeaderHandler { InnerHandler = _netz }) { BaseAddress = new Uri("http://localhost/") });
        Services.AddScoped<IRezeptApi, RezeptApi>();
    }

    [Fact]
    public async Task RezeptApi_meldet_eine_abgebrochene_Verbindung_als_ApiFehler()
    {
        var api = Services.GetRequiredService<IRezeptApi>();

        var fehler = await Should.ThrowAsync<Exception>(() => api.AuflistenAsync(null, Xunit.TestContext.Current.CancellationToken));

        fehler.ShouldBeOfType<ApiFehlerException>();
    }

    [Fact]
    public void Rezeptliste_zeigt_bei_abgebrochener_Verbindung_eine_Meldung()
    {
        var seite = Render<Rezepte>(p => p.AddCascadingValue(new SitzungsInfo("Anna", "t", new Berechtigungen(true, true, true), null)));

        seite.WaitForAssertion(() => seite.Find("[role=alert]").TextContent.ShouldNotBeNullOrWhiteSpace());
    }

    [Fact]
    public void Entwurf_speichern_bei_abgebrochener_Verbindung_zeigt_Meldung_und_behaelt_die_Eingabe()
    {
        _netz.NurAenderungenScheitern = true;
        var seite = Render<Entwurf>(p => p
            .AddCascadingValue(new SitzungsInfo("Anna", "t", new Berechtigungen(true, true, false), null))
            .Add(s => s.Id, Netz.RezeptId));
        seite.WaitForAssertion(() => seite.FindAll(".gramm input").Count.ShouldBe(1));

        seite.FindAll(".gramm input")[0].Input("150");
        seite.Find("form").Submit();

        seite.WaitForAssertion(() => seite.Find("[role=alert]").TextContent.ShouldNotBeNullOrWhiteSpace());
        seite.FindAll(".gramm input")[0].GetAttribute("value").ShouldBe("150");
    }

    protected override void Dispose(bool disposing)
    {
        _netz.Dispose();
        base.Dispose(disposing);
    }

    private sealed class Netz : HttpMessageHandler
    {
        public static readonly Guid RezeptId = Guid.Parse("0195f0a6-0000-7000-8000-0000000000aa");

        /// <summary>Lesen klappt noch, erst das Speichern trifft das Funkloch.</summary>
        public bool NurAenderungenScheitern { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!NurAenderungenScheitern || request.Method != HttpMethod.Get)
            {
                throw new HttpRequestException("Die Verbindung wurde unterbrochen.");
            }

            var pfad = request.RequestUri!.AbsolutePath;
            var json = pfad.EndsWith("/fassungen/2", StringComparison.Ordinal)
                ? $$$"""{"rezeptId":"{{{RezeptId}}}","nummer":2,"status":"entwurf","erstelltAm":"2026-11-24T10:00:00Z","freigegebenAm":null,"bezugsform":null,"zutaten":[{"bezeichnung":"Butter","gramm":140}],"gesamtgewichtGramm":140}"""
                : $$$"""{"id":"{{{RezeptId}}}","name":"Sachertorte","zutaten":[{"bezeichnung":"Butter","gramm":140}],"gesamtgewichtGramm":140,"bezugsform":null,"massgeblicheFassung":1,"aktuelleFassung":1,"entwurf":2,"fassungen":[]}""";
            var antwort = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            antwort.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"5\"");
            return Task.FromResult(antwort);
        }
    }
}

/// <summary>Unabhängiger Test: Beim schnellen Tippen überholen sich Suchanfragen.</summary>
public sealed class SuchwettlaufTests : BunitContext
{
    private readonly GesteuerteApi _api = new();

    public SuchwettlaufTests() => Services.AddSingleton<IRezeptApi>(_api);

    private static readonly RezeptUebersicht Sacher = new(Guid.NewGuid(), "Sachertorte", 1, 1, null);
    private static readonly RezeptUebersicht Linzer = new(Guid.NewGuid(), "Linzer Torte", 1, 1, null);

    private async Task<IRenderedComponent<Rezepte>> ListeAsync()
    {
        var seite = Render<Rezepte>(p => p.AddCascadingValue(new SitzungsInfo("Anna", "t", new Berechtigungen(true, false, false), null)));
        await seite.InvokeAsync(() => _api.Offen[0].SetResult([Linzer, Sacher]));
        return seite;
    }

    [Fact]
    public async Task Spaete_Antwort_auf_eine_alte_Suche_ueberschreibt_die_neue_nicht()
    {
        var seite = await ListeAsync();
        seite.Find("input.suche").Input("s");
        seite.Find("input.suche").Input("sacher");

        await seite.InvokeAsync(() => _api.Offen[2].SetResult([Sacher]));
        await seite.InvokeAsync(() => _api.Offen[1].SetResult([Linzer, Sacher]));

        seite.FindAll(".rezeptliste .name").Select(e => e.TextContent).ShouldBe(["Sachertorte"]);
    }

    [Fact]
    public async Task Scheitert_eine_ueberholte_Suche_bleibt_die_Seite_bedienbar()
    {
        var seite = await ListeAsync();
        seite.Find("input.suche").Input("s");
        seite.Find("input.suche").Input("sacher");

        await seite.InvokeAsync(() => _api.Offen[2].SetResult([Sacher]));
        await seite.InvokeAsync(() => _api.Offen[1].SetException(new ApiFehlerException(503, null, "Server nicht erreichbar.")));

        if (Renderer.UnhandledException.IsCompleted)
        {
            var fehler = await Renderer.UnhandledException;
            fehler.ShouldBeNull("Unbehandelte Ausnahme in der Seite (Blazor zeigt dann den Fehlerbalken)");
        }
        seite.FindAll(".rezeptliste .name").Select(e => e.TextContent).ShouldBe(["Sachertorte"]);
    }

    private sealed class GesteuerteApi : IRezeptApi
    {
        public List<TaskCompletionSource<IReadOnlyList<RezeptUebersicht>>> Offen { get; } = [];

        public Task<IReadOnlyList<RezeptUebersicht>> AuflistenAsync(string? suche, CancellationToken cancellationToken = default)
        {
            var antwort = new TaskCompletionSource<IReadOnlyList<RezeptUebersicht>>();
            Offen.Add(antwort);
            return antwort.Task;
        }

        public Task<Berechtigungen> BerechtigungenAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MitStand<RezeptDetails>?> RezeptAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MitStand<FassungDetails>?> FassungAsync(Guid id, int nummer, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MitStand<RezeptDetails>> AnlegenAsync(string name, RezeptInhalt inhalt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SkaliertesRezept> UmrechnenAsync(Guid id, Umrechnung ziel, int? fassung, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MitStand<FassungDetails>> EntwurfAnlegenAsync(Guid id, string stand, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MitStand<FassungDetails>> EntwurfSpeichernAsync(Guid id, string stand, RezeptInhalt inhalt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task EntwurfVerwerfenAsync(Guid id, string stand, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MitStand<FassungDetails>> FreigebenAsync(Guid id, string stand, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
