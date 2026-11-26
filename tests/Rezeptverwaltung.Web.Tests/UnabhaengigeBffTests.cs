extern alias WebHost;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Anmeldung = WebHost::Rezeptverwaltung.Web.Anmeldung;

namespace Rezeptverwaltung.Web.Tests;

/// <summary>
/// Unabhängiger Test der Anmeldung nach dem BFF-Muster (Anforderung 2): CSRF hält Anfragen vor der API auf,
/// Abmelden nur per POST mit dem eigenen Antiforgery-Token, nur lokale Rücksprungadressen.
/// </summary>
public sealed class UnabhaengigeBffTests(WebFactory web) : IClassFixture<WebFactory>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static object Rezept(string name) => new { name, zutaten = new[] { new { bezeichnung = "Mehl", gramm = 250 } } };

    [Theory]
    [InlineData("/rezepte/0195f0a6-0000-7000-8000-000000000000/entwurf")]
    [InlineData("/rezepte/0195f0a6-0000-7000-8000-000000000000/fassungen/1")]
    [InlineData("/gibt-es-nicht")]
    public async Task Jede_Seite_ohne_Sitzung_leitet_zum_Login(string seite)
    {
        using var browser = web.CreateBrowser();

        using var antwort = await browser.GetAsync(seite, _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Found);
        antwort.Headers.Location!.ToString().ShouldStartWith($"{TestLogin.Authority}/authorize?");
    }

    [Fact]
    public async Task Aendernder_Aufruf_ohne_Sitzung_bekommt_401_statt_Weiterleitung()
    {
        using var browser = web.CreateBrowser();
        browser.DefaultRequestHeaders.Add("X-CSRF", "1");

        using var antwort = await browser.PostAsJsonAsync("/api/v1/rezepte", Rezept("Ohne Sitzung"), _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        antwort.Headers.Location.ShouldBeNull();
    }

    [Fact]
    public async Task Ohne_CSRF_Header_erreicht_eine_Aenderung_die_API_nicht()
    {
        using var browser = await web.AngemeldetAlsAsync(Persona.InhaberA);
        var name = $"CSRF {Guid.NewGuid():N}";
        browser.DefaultRequestHeaders.Remove("X-CSRF");

        using var antwort = await browser.PostAsJsonAsync("/api/v1/rezepte", Rezept(name), _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        browser.DefaultRequestHeaders.Add("X-CSRF", "1");
        var liste = await browser.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte?suche={Uri.EscapeDataString(name)}", _ct);
        liste.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Abmelden_per_GET_beendet_die_Sitzung_nicht()
    {
        using var browser = await web.AngemeldetAlsAsync(Persona.InhaberA);

        using var _ = await browser.GetAsync("/abmelden", _ct);

        (await browser.GetAsync("/bff/benutzer", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Abmelde_Token_einer_anderen_Sitzung_gilt_nicht()
    {
        using var anna = await web.AngemeldetAlsAsync(Persona.InhaberA);
        using var konrad = await web.AngemeldetAlsAsync(Persona.KonditorA);
        await konrad.GetAsync("/bff/benutzer", _ct); // Konrad hat ein eigenes Antiforgery-Cookie
        var annasToken = (await anna.GetFromJsonAsync<JsonElement>("/bff/benutzer", _ct)).GetProperty("abmeldeToken").GetString()!;

        using var antwort = await konrad.PostAsync(
            "/abmelden", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = annasToken }), _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await konrad.GetAsync("/bff/benutzer", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anna.GetAsync("/bff/benutzer", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/\t/boese.example/")] // Browser entfernen Tabulatoren aus Adressen: wird zu //boese.example
    [InlineData("/\n/boese.example/")]
    [InlineData("\\\\boese.example")]
    [InlineData("https:/boese.example")]
    [InlineData(" //boese.example")]
    public void Rueckspruenge_die_ein_Browser_als_fremde_Seite_liest_werden_abgewiesen(string eingabe) =>
        Anmeldung.NurLokal(eingabe).ShouldBe("/");

    [Fact]
    public async Task Login_mit_kodierter_fremder_Adresse_landet_auf_der_Startseite()
    {
        using var browser = web.CreateBrowser();

        var ziel = await WebFactory.AnmeldenAsync(browser, Persona.InhaberA, returnUrl: "/\t/boese.example/");

        ziel.ShouldBe("/");
    }

    [Fact]
    public async Task Nach_dem_Abmelden_wird_kein_Token_mehr_weitergeleitet()
    {
        using var browser = await web.AngemeldetAlsAsync(Persona.InhaberA);
        var token = (await browser.GetFromJsonAsync<JsonElement>("/bff/benutzer", _ct)).GetProperty("abmeldeToken").GetString()!;
        using var abmelden = await browser.PostAsync(
            "/abmelden", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }), _ct);
        var vorher = web.Mitschnitt.LetzteKopfzeilen;

        using var antwort = await browser.PostAsJsonAsync("/api/v1/rezepte", Rezept("Nach dem Abmelden"), _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        web.Mitschnitt.LetzteKopfzeilen.ShouldBeSameAs(vorher);
    }
}
