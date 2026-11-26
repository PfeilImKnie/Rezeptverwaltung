extern alias WebHost;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Anmeldung = WebHost::Rezeptverwaltung.Web.Anmeldung;

namespace Rezeptverwaltung.Web.Tests;

/// <summary>
/// Weboberfläche als Backend for Frontend (ADR 0016): Login per OIDC, Tokens nur auf dem Server, Weiterleitung an
/// die API mit Bearer-Token, Schutz vor CSRF. Alles gegen den echten Login-Server und die echte API.
/// </summary>
public sealed class BffTests(WebFactory web) : IClassFixture<WebFactory>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static readonly object Biskuit = new
    {
        name = "Biskuit",
        zutaten = new[] { new { bezeichnung = "Eier", gramm = 300 }, new { bezeichnung = "Zucker", gramm = 150 } },
        bezugsform = new { art = "ring", durchmesserCm = 26, hoeheCm = 6 },
    };

    [Theory]
    [InlineData("/")]
    [InlineData("/rezepte/neu")]
    [InlineData("/rezepte/0195f0a6-0000-7000-8000-000000000000")]
    public async Task Seite_ohne_Anmeldung_leitet_zum_Login_weiter(string seite)
    {
        using var browser = web.CreateBrowser();

        using var antwort = await browser.GetAsync(seite, _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Found);
        var ziel = antwort.Headers.Location!.ToString();
        ziel.ShouldStartWith($"{TestLogin.Authority}/authorize?");
        ziel.ShouldContain($"client_id={TestLogin.WebClientId}");
        ziel.ShouldContain("code_challenge_method=S256");
        ziel.ShouldContain("response_type=code");
        ziel.ShouldContain("offline_access");
    }

    [Theory]
    [InlineData("/api/v1/rezepte")]
    [InlineData("/bff/benutzer")]
    public async Task Programmatische_Aufrufe_ohne_Anmeldung_bekommen_401_statt_Weiterleitung(string pfad)
    {
        using var browser = web.CreateBrowser();
        browser.DefaultRequestHeaders.Add("X-CSRF", "1");

        using var antwort = await browser.GetAsync(pfad, _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        antwort.Headers.Location.ShouldBeNull();
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/manifest.webmanifest")]
    public async Task Gesundheit_und_statische_Dateien_brauchen_keine_Anmeldung(string pfad)
    {
        using var browser = web.CreateBrowser();

        using var antwort = await browser.GetAsync(pfad, _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nach_dem_Login_kennt_die_Oberflaeche_den_Namen()
    {
        using var browser = await web.AngemeldetAlsAsync(Persona.KonditorA, "Konrad Konditor");

        var benutzer = await browser.GetFromJsonAsync<JsonElement>("/bff/benutzer", _ct);

        benutzer.GetProperty("name").GetString().ShouldBe("Konrad Konditor");
        benutzer.GetProperty("abmeldeToken").GetString().ShouldNotBeNullOrWhiteSpace();
        (await browser.GetAsync("/", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Im_Browser_liegt_kein_Token()
    {
        using var browser = web.CreateBrowser();
        browser.DefaultRequestHeaders.Add("X-CSRF", "1");
        await WebFactory.AnmeldenAsync(browser, Persona.InhaberA);
        await browser.GetAsync("/api/v1/rezepte", _ct);
        var accessToken = web.Mitschnitt.LetzteKopfzeilen!.Authorization!.Parameter!;

        var benutzer = await browser.GetStringAsync("/bff/benutzer", _ct);
        var html = await browser.GetStringAsync("/", _ct);
        // Blazor legt Zustand als Base64-JSON in die Seite; auch der darf keinen Token enthalten.
        var zustand = System.Text.RegularExpressions.Regex.Matches(html, "<!--Blazor-[A-Za-z-]+:(?<b>[A-Za-z0-9+/=]+)-->")
            .Select(m => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups["b"].Value)));

        foreach (var text in zustand.Append(benutzer).Append(html))
        {
            text.ShouldNotContain(accessToken);
            text.ShouldNotContain(accessToken.Split('.')[2]); // Signatur
            text.ShouldNotContain("access_token");
            text.ShouldNotContain("refresh_token");
            text.ShouldNotContain("id_token");
        }
    }

    [Fact]
    public async Task Sitzungs_Cookie_ist_HttpOnly_Secure_und_SameSite_Lax()
    {
        using var browser = web.CreateBrowser();
        using var start = await browser.GetAsync("/anmelden", _ct);
        using var loginServer = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var formular = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = Persona.InhaberA.Name,
            ["claims"] = TestLogin.LoginClaims(Persona.InhaberA),
        });
        using var login = await loginServer.PostAsync(start.Headers.Location, formular, _ct);
        var html = await login.Content.ReadAsStringAsync(_ct);
        var felder = System.Text.RegularExpressions.Regex.Matches(html, "name=\"(?<n>code|state)\" value=\"(?<v>[^\"]*)\"")
            .ToDictionary(m => m.Groups["n"].Value, m => WebUtility.HtmlDecode(m.Groups["v"].Value));

        using var rueck = await browser.PostAsync("/signin-oidc", new FormUrlEncodedContent(felder), _ct);

        var sitzung = rueck.Headers.GetValues("Set-Cookie").Where(c => c.StartsWith(Anmeldung.CookieName, StringComparison.Ordinal)).ToList();
        sitzung.ShouldNotBeEmpty();
        sitzung.ShouldAllBe(c => c.Contains("httponly", StringComparison.OrdinalIgnoreCase)
            && c.Contains("secure", StringComparison.OrdinalIgnoreCase)
            && c.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase)
            && c.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Api_Aufrufe_gehen_mit_Token_aber_ohne_Cookie_an_die_API()
    {
        using var browser = await web.AngemeldetAlsAsync(Persona.KonditorA);

        using var antwort = await browser.PostAsJsonAsync("/api/v1/rezepte", Biskuit, _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
        antwort.Headers.ETag.ShouldNotBeNull();
        var kopf = web.Mitschnitt.LetzteKopfzeilen!;
        kopf.Authorization!.Scheme.ShouldBe("Bearer");
        kopf.Authorization.Parameter.ShouldStartWith("eyJ");
        kopf.Contains("Cookie").ShouldBeFalse();
        kopf.Contains("X-CSRF").ShouldBeFalse();
        web.Mitschnitt.LetzteAdresse!.ToString().ShouldBe($"{WebFactory.ApiAdresse}/api/v1/rezepte");
    }

    [Fact]
    public async Task Ein_mitgeschickter_fremder_Token_wird_ersetzt()
    {
        using var browser = await web.AngemeldetAlsAsync(Persona.MitarbeiterA);
        var inhaberToken = await TestLogin.TokenAsync(Persona.InhaberA);
        browser.DefaultRequestHeaders.Authorization = TestLogin.Header(inhaberToken);

        using var antwort = await browser.PostAsJsonAsync("/api/v1/rezepte", Biskuit, _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        web.Mitschnitt.LetzteKopfzeilen!.Authorization!.Parameter.ShouldNotBe(inhaberToken);
    }

    [Fact]
    public async Task Ganzer_Ablauf_ueber_die_Oberflaeche_Anlegen_Umrechnen_Freigeben()
    {
        using var konditor = await web.AngemeldetAlsAsync(Persona.KonditorA);
        using var angelegt = await konditor.PostAsJsonAsync("/api/v1/rezepte", Biskuit, _ct);
        var id = (await angelegt.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();

        var umgerechnet = await konditor.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert/ring?durchmesserCm=18&hoeheCm=6", _ct);
        umgerechnet.GetProperty("zutaten")[0].GetProperty("gramm").GetDecimal().ShouldBe(143.79m);

        using var konditorFreigabe = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rezepte/{id}/entwurf/freigabe");
        konditorFreigabe.Headers.TryAddWithoutValidation("If-Match", angelegt.Headers.ETag!.ToString());
        (await konditor.SendAsync(konditorFreigabe, _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var inhaber = await web.AngemeldetAlsAsync(Persona.InhaberA);
        using var freigabe = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rezepte/{id}/entwurf/freigabe");
        freigabe.Headers.TryAddWithoutValidation("If-Match", angelegt.Headers.ETag!.ToString());
        using var freigegeben = await inhaber.SendAsync(freigabe, _ct);
        freigegeben.StatusCode.ShouldBe(HttpStatusCode.OK);

        var liste = await inhaber.GetFromJsonAsync<JsonElement>("/api/v1/rezepte", _ct);
        liste.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id).GetProperty("aktuelleFassung").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Rezepte_anderer_Betriebe_bleiben_auch_ueber_die_Oberflaeche_unsichtbar()
    {
        using var a = await web.AngemeldetAlsAsync(Persona.InhaberA);
        using var angelegt = await a.PostAsJsonAsync("/api/v1/rezepte", Biskuit, _ct);
        var id = (await angelegt.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();

        using var b = await web.AngemeldetAlsAsync(Persona.InhaberB);

        (await b.GetAsync($"/api/v1/rezepte/{id}", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.GetFromJsonAsync<JsonElement>("/api/v1/rezepte", _ct)).EnumerateArray()
            .ShouldNotContain(r => r.GetProperty("id").GetGuid() == id);
    }

    [Theory]
    [InlineData("/api/v1/rezepte")]
    [InlineData("/bff/benutzer")]
    public async Task Ohne_CSRF_Header_wird_abgewiesen(string pfad)
    {
        using var browser = await web.AngemeldetAlsAsync(Persona.InhaberA);
        browser.DefaultRequestHeaders.Remove("X-CSRF");

        using var antwort = await browser.GetAsync(pfad, _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("")]
    public async Task CSRF_Header_muss_genau_1_sein(string wert)
    {
        using var browser = await web.AngemeldetAlsAsync(Persona.InhaberA);
        browser.DefaultRequestHeaders.Remove("X-CSRF");
        browser.DefaultRequestHeaders.Add("X-CSRF", wert);

        using var antwort = await browser.GetAsync("/api/v1/rezepte", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Fremde_Seite_kann_niemanden_abmelden()
    {
        using var browser = await web.AngemeldetAlsAsync(Persona.InhaberA);

        using var antwort = await browser.PostAsync("/abmelden", new FormUrlEncodedContent([]), _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await browser.GetAsync("/bff/benutzer", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Abmelden_beendet_die_Sitzung_und_meldet_beim_Login_Server_ab()
    {
        using var browser = await web.AngemeldetAlsAsync(Persona.InhaberA);
        var token = (await browser.GetFromJsonAsync<JsonElement>("/bff/benutzer", _ct)).GetProperty("abmeldeToken").GetString()!;

        using var antwort = await browser.PostAsync(
            "/abmelden", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }), _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Found);
        antwort.Headers.Location!.ToString().ShouldStartWith($"{TestLogin.Authority}/endsession");
        (await browser.GetAsync("/bff/benutzer", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await browser.GetAsync("/api/v1/rezepte", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rueckkehr_nach_dem_Login_zur_gewuenschten_Seite()
    {
        using var browser = web.CreateBrowser();

        var ziel = await WebFactory.AnmeldenAsync(browser, Persona.InhaberA, returnUrl: "/rezepte/neu");

        ziel.ShouldBe("/rezepte/neu");
    }

    [Theory]
    [InlineData("https://boese.example/")]
    [InlineData("//boese.example/")]
    [InlineData("/\\boese.example/")]
    [InlineData("javascript:alert(1)")]
    public async Task Login_leitet_nie_auf_fremde_Seiten_weiter(string returnUrl)
    {
        using var browser = web.CreateBrowser();

        var ziel = await WebFactory.AnmeldenAsync(browser, Persona.InhaberA, returnUrl: returnUrl);

        ziel.ShouldBe("/");
    }

    [Fact]
    public async Task Kurz_vor_Ablauf_wird_der_Access_Token_erneuert_ohne_neue_Anmeldung()
    {
        using var browser = await web.AngemeldetAlsAsync(Persona.InhaberA);
        (await browser.GetAsync("/api/v1/rezepte", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var vorher = web.Mitschnitt.LetzteKopfzeilen!.Authorization!.Parameter;

        web.Zeit.Versatz = TimeSpan.FromMinutes(59); // Token des Login-Servers gilt 60 Minuten, erneuert wird 2 Minuten vorher
        try
        {
            using var antwort = await browser.GetAsync("/api/v1/rezepte", _ct);

            antwort.StatusCode.ShouldBe(HttpStatusCode.OK);
            antwort.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith(Anmeldung.CookieName, StringComparison.Ordinal));
            web.Mitschnitt.LetzteKopfzeilen!.Authorization!.Parameter.ShouldNotBe(vorher);
        }
        finally
        {
            web.Zeit.Versatz = TimeSpan.Zero;
        }
    }

    [Fact]
    public async Task Gleichzeitige_Anfragen_kurz_vor_Ablauf_teilen_sich_eine_Erneuerung()
    {
        using var browser = await web.AngemeldetAlsAsync(Persona.InhaberA);
        await browser.GetAsync("/api/v1/rezepte", _ct);
        var vorher = web.Mitschnitt.LetzteKopfzeilen!.Authorization!.Parameter;
        web.Mitschnitt.Tokens.Clear();

        web.Zeit.Versatz = TimeSpan.FromMinutes(59);
        try
        {
            // Alle mit demselben alten Cookie, wie beim schnellen Tippen in der Suche.
            var antworten = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => browser.GetAsync("/api/v1/rezepte", _ct)));

            antworten.ShouldAllBe(a => a.StatusCode == HttpStatusCode.OK);
            var tokens = web.Mitschnitt.Tokens.ToList();
            tokens.Count.ShouldBe(8);
            tokens.Distinct().ShouldHaveSingleItem().ShouldNotBe(vorher); // genau eine Erneuerung, ein neuer Token
        }
        finally
        {
            web.Zeit.Versatz = TimeSpan.Zero;
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)] // Refresh Token abgelehnt (z. B. widerrufen)
    [InlineData((HttpStatusCode)0)] // Login-Server nicht erreichbar
    [InlineData(HttpStatusCode.OK)] // HTML-Fehlerseite statt JSON
    public async Task Scheitert_die_Erneuerung_gilt_der_alte_Token_bis_zum_Ablauf_danach_endet_die_Sitzung(HttpStatusCode stoerung)
    {
        using var browser = await web.AngemeldetAlsAsync(Persona.InhaberA);
        await browser.GetAsync("/api/v1/rezepte", _ct);
        var vorher = web.Mitschnitt.LetzteKopfzeilen!.Authorization!.Parameter;
        web.LoginVerbindung.ErneuerungStoeren = stoerung;
        try
        {
            web.Zeit.Versatz = TimeSpan.FromMinutes(59); // Token gilt noch 1 Minute, Erneuerung wird versucht
            using (var nochGueltig = await browser.GetAsync("/api/v1/rezepte", _ct))
            {
                nochGueltig.StatusCode.ShouldBe(HttpStatusCode.OK);
                web.Mitschnitt.LetzteKopfzeilen!.Authorization!.Parameter.ShouldBe(vorher);
            }

            web.Zeit.Versatz = TimeSpan.FromMinutes(61); // abgelaufen, Erneuerung scheitert weiter
            using (var abgelaufen = await browser.GetAsync("/api/v1/rezepte", _ct))
            {
                abgelaufen.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            using var seite = await browser.GetAsync("/", _ct);
            seite.StatusCode.ShouldBe(HttpStatusCode.Found);
            seite.Headers.Location!.ToString().ShouldStartWith($"{TestLogin.Authority}/authorize?");
        }
        finally
        {
            web.Zeit.Versatz = TimeSpan.Zero;
            web.LoginVerbindung.ErneuerungStoeren = null;
        }
    }

    [Fact]
    public async Task Nach_einem_kurzen_Ausfall_klappt_die_naechste_Erneuerung()
    {
        using var browser = await web.AngemeldetAlsAsync(Persona.InhaberA);
        await browser.GetAsync("/api/v1/rezepte", _ct);
        var vorher = web.Mitschnitt.LetzteKopfzeilen!.Authorization!.Parameter;
        web.Zeit.Versatz = TimeSpan.FromMinutes(59);
        try
        {
            web.LoginVerbindung.ErneuerungStoeren = 0;
            (await browser.GetAsync("/api/v1/rezepte", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

            web.LoginVerbindung.ErneuerungStoeren = null;
            (await browser.GetAsync("/api/v1/rezepte", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

            web.Mitschnitt.LetzteKopfzeilen!.Authorization!.Parameter.ShouldNotBe(vorher);
        }
        finally
        {
            web.Zeit.Versatz = TimeSpan.Zero;
            web.LoginVerbindung.ErneuerungStoeren = null;
        }
    }

    [Fact]
    public async Task Ohne_Ablauf_bleibt_der_Token_derselbe()
    {
        using var browser = await web.AngemeldetAlsAsync(Persona.InhaberA);
        await browser.GetAsync("/api/v1/rezepte", _ct);
        var vorher = web.Mitschnitt.LetzteKopfzeilen!.Authorization!.Parameter;

        await browser.GetAsync("/api/v1/rezepte", _ct);

        web.Mitschnitt.LetzteKopfzeilen!.Authorization!.Parameter.ShouldBe(vorher);
    }
}
