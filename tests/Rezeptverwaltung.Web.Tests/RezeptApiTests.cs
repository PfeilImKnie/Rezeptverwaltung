using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Rezeptverwaltung.Web.Client.Api;

namespace Rezeptverwaltung.Web.Tests;

/// <summary>Der HTTP-Zugriff des Clients: Adressen, If-Match, CSRF-Header, Fehler und abgelaufene Sitzung.</summary>
public sealed class RezeptApiTests : IDisposable
{
    private readonly BunitContext _bunit = new();
    private readonly Aufzeichnung _server = new();
    private readonly RezeptApi _api;
    private readonly CancellationToken _ct = Xunit.TestContext.Current.CancellationToken;
    private static readonly Guid Id = Guid.Parse("0195f0a6-0000-7000-8000-000000000001");

    public RezeptApiTests()
    {
        var http = new HttpClient(new CsrfHeaderHandler { InnerHandler = _server }) { BaseAddress = new Uri("https://rezepte.test/") };
        _api = new RezeptApi(http, _bunit.Services.GetRequiredService<NavigationManager>());
    }

    public void Dispose()
    {
        _bunit.Dispose();
        _server.Dispose();
    }

    private const string FassungJson =
        """{"rezeptId":"0195f0a6-0000-7000-8000-000000000001","nummer":2,"status":"entwurf","erstelltAm":"2026-11-24T10:00:00Z","freigegebenAm":null,"bezugsform":null,"zutaten":[],"gesamtgewichtGramm":0}""";

    [Fact]
    public async Task Jede_Anfrage_traegt_den_CSRF_Header()
    {
        _server.Antwort = Json(HttpStatusCode.OK, "[]");

        await _api.AuflistenAsync(null, _ct);

        _server.Letzte!.Headers.GetValues("X-CSRF").ShouldBe(["1"]);
    }

    [Theory]
    [InlineData(null, "https://rezepte.test/api/v1/rezepte")]
    [InlineData("  ", "https://rezepte.test/api/v1/rezepte")]
    [InlineData("Käse & Sahne", "https://rezepte.test/api/v1/rezepte?suche=K%C3%A4se%20%26%20Sahne")]
    public async Task Suche_steht_kodiert_in_der_Adresse(string? suche, string adresse)
    {
        _server.Antwort = Json(HttpStatusCode.OK, """[{"id":"0195f0a6-0000-7000-8000-000000000001","name":"Käsekuchen","massgeblicheFassung":1,"aktuelleFassung":null,"entwurf":1}]""");

        var liste = await _api.AuflistenAsync(suche, _ct);

        _server.Letzte!.RequestUri!.AbsoluteUri.ShouldBe(adresse);
        liste.ShouldBe([new RezeptUebersicht(Id, "Käsekuchen", 1, null, 1)]);
    }

    [Fact]
    public async Task Zahlen_in_der_Adresse_immer_mit_Punkt_auch_bei_deutscher_Sprache()
    {
        var vorher = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            _server.Antwort = Json(HttpStatusCode.OK, """{"rezeptId":"0195f0a6-0000-7000-8000-000000000001","name":"B","fassung":1,"faktor":0.5,"zielform":null,"zielgewichtGramm":12.5,"zutaten":[]}""");

            await _api.UmrechnenAsync(Id, new Umrechnung.AufGewicht(12.5m), null, _ct);
            _server.Letzte!.RequestUri!.PathAndQuery.ShouldBe($"/api/v1/rezepte/{Id}/skaliert?zielgewichtGramm=12.5");

            await _api.UmrechnenAsync(Id, new Umrechnung.AufForm(Form.AlsRing(18.5m, 6)), 3, _ct);
            _server.Letzte!.RequestUri!.PathAndQuery.ShouldBe($"/api/v1/rezepte/{Id}/skaliert/ring?durchmesserCm=18.5&hoeheCm=6&fassung=3");

            await _api.UmrechnenAsync(Id, new Umrechnung.AufForm(Form.AlsBlech(40, 30.5m, 2.5m)), null, _ct);
            _server.Letzte!.RequestUri!.PathAndQuery.ShouldBe($"/api/v1/rezepte/{Id}/skaliert/blech?laengeCm=40&breiteCm=30.5&hoeheCm=2.5");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = vorher;
        }
    }

    [Fact]
    public async Task Aenderungen_schicken_den_Stand_als_If_Match()
    {
        _server.Antwort = Json(HttpStatusCode.OK, FassungJson, etag: "\"8\"");

        var ergebnis = await _api.EntwurfSpeichernAsync(Id, "\"7\"", new RezeptInhalt([new Zutat("Butter", 140.5m)], null), _ct);

        _server.Letzte!.Method.ShouldBe(HttpMethod.Put);
        _server.Letzte.Headers.GetValues("If-Match").ShouldBe(["\"7\""]);
        _server.LetzterKoerper.ShouldBe("""{"zutaten":[{"bezeichnung":"Butter","gramm":140.5}]}"""); // ohne Form: Feld fehlt
        ergebnis.Stand.ShouldBe("\"8\"");
    }

    [Fact]
    public async Task Anlegen_schickt_Name_Zutaten_und_Form()
    {
        _server.Antwort = Json(HttpStatusCode.Created, """{"id":"0195f0a6-0000-7000-8000-000000000001","name":"Torte","zutaten":[],"gesamtgewichtGramm":0,"bezugsform":null,"massgeblicheFassung":1,"aktuelleFassung":null,"entwurf":1,"fassungen":[]}""", etag: "\"1\"");

        var rezept = await _api.AnlegenAsync("Torte", new RezeptInhalt([new Zutat("Eier", 300)], Form.AlsRing(26, 6)), _ct);

        _server.LetzterKoerper.ShouldBe("""{"name":"Torte","zutaten":[{"bezeichnung":"Eier","gramm":300}],"bezugsform":{"art":"ring","hoeheCm":6,"durchmesserCm":26}}""");
        rezept.Wert.Id.ShouldBe(Id);
        rezept.Stand.ShouldBe("\"1\"");
    }

    [Fact]
    public async Task Nicht_vorhanden_ist_null_statt_Fehler()
    {
        _server.Antwort = Json(HttpStatusCode.NotFound, """{"title":"Not Found","status":404}""");

        (await _api.RezeptAsync(Id, _ct)).ShouldBeNull();
        (await _api.FassungAsync(Id, 3, _ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Fachlicher_Fehler_zeigt_die_Meldung_der_API()
    {
        _server.Antwort = Json(HttpStatusCode.UnprocessableEntity, """{"title":"Fachliche Regel verletzt","detail":"Ein Gewicht darf höchstens zwei Nachkommastellen haben.","code":"gewicht.zu_genau","status":422}""");

        var fehler = await Should.ThrowAsync<ApiFehlerException>(() => _api.AnlegenAsync("x", new RezeptInhalt([], null), _ct));

        fehler.Status.ShouldBe(422);
        fehler.Code.ShouldBe("gewicht.zu_genau");
        fehler.Message.ShouldBe("Ein Gewicht darf höchstens zwei Nachkommastellen haben.");
        fehler.IstVeraltet.ShouldBeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, """{"code":"recht.fehlt","detail":"technisch"}""", "Dafür fehlt dir das Recht.", false)]
    [InlineData(HttpStatusCode.Forbidden, """{"code":"betrieb.nicht_eindeutig","detail":"Die Anmeldung gilt für mehrere Betriebe."}""", "Die Anmeldung gilt für mehrere Betriebe.", false)]
    [InlineData(HttpStatusCode.PreconditionFailed, """{"code":"rezept.veraltet"}""", "Das Rezept wurde inzwischen an anderer Stelle geändert. Bitte neu laden.", true)]
    [InlineData(HttpStatusCode.Conflict, """{"code":"rezept.gleichzeitig_geaendert","detail":"Bitte neu laden."}""", "Bitte neu laden.", true)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "", "Die Eingabe ist zu groß.", false)]
    [InlineData(HttpStatusCode.BadGateway, "<html>Bad Gateway</html>", "Das hat nicht geklappt (Fehler 502). Bitte später noch einmal versuchen.", false)]
    public async Task Fehler_werden_verstaendlich(HttpStatusCode status, string koerper, string meldung, bool veraltet)
    {
        _server.Antwort = Json(status, koerper);

        var fehler = await Should.ThrowAsync<ApiFehlerException>(() => _api.FreigebenAsync(Id, "\"1\"", _ct));

        fehler.Message.ShouldBe(meldung);
        fehler.IstVeraltet.ShouldBe(veraltet);
    }

    [Fact]
    public async Task Abgelaufene_Sitzung_fuehrt_zum_Login_und_danach_hierher_zurueck()
    {
        var navigation = _bunit.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"rezepte/{Id}?x=1");
        _server.Antwort = Json(HttpStatusCode.Unauthorized, "");

        var fehler = await Should.ThrowAsync<ApiFehlerException>(() => _api.RezeptAsync(Id, _ct));

        fehler.Status.ShouldBe(401);
        navigation.Uri.ShouldBe($"http://localhost/anmelden?returnUrl={Uri.EscapeDataString($"/rezepte/{Id}?x=1")}");
    }

    [Fact]
    public async Task Verwerfen_und_Entwurf_anlegen_schicken_den_Stand()
    {
        _server.Antwort = Json(HttpStatusCode.NoContent, "");
        await _api.EntwurfVerwerfenAsync(Id, "\"3\"", _ct);
        _server.Letzte!.Method.ShouldBe(HttpMethod.Delete);
        _server.Letzte.Headers.GetValues("If-Match").ShouldBe(["\"3\""]);

        _server.Antwort = Json(HttpStatusCode.Created, FassungJson, etag: "\"4\"");
        var entwurf = await _api.EntwurfAnlegenAsync(Id, "\"3\"", _ct);
        _server.Letzte!.RequestUri!.PathAndQuery.ShouldBe($"/api/v1/rezepte/{Id}/entwurf");
        entwurf.Wert.IstEntwurf.ShouldBeTrue();
        entwurf.Stand.ShouldBe("\"4\"");
    }

    [Fact]
    public async Task Berechtigungen_werden_gelesen()
    {
        _server.Antwort = Json(HttpStatusCode.OK, """{"lesen":true,"bearbeiten":true,"freigeben":false}""");

        (await _api.BerechtigungenAsync(_ct)).ShouldBe(new Berechtigungen(true, true, false));
        _server.Letzte!.RequestUri!.PathAndQuery.ShouldBe("/api/v1/rezepte/berechtigungen");
    }

    private static Func<HttpResponseMessage> Json(HttpStatusCode status, string koerper, string? etag = null) => () =>
    {
        var antwort = new HttpResponseMessage(status) { Content = new StringContent(koerper, Encoding.UTF8, "application/json") };
        if (etag is not null)
        {
            antwort.Headers.ETag = System.Net.Http.Headers.EntityTagHeaderValue.Parse(etag);
        }

        return antwort;
    };

    private sealed class Aufzeichnung : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Antwort { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK);

        public HttpRequestMessage? Letzte { get; private set; }

        public string? LetzterKoerper { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Letzte = request;
            LetzterKoerper = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return Antwort();
        }
    }
}
