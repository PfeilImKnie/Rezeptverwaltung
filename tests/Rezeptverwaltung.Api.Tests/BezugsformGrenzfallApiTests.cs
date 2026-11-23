using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>
/// Unabhängiger Test der Bezugsform über die API: Blech-Endpunkt, Grenzwerte, Wahl der Fassung, Bearbeiten des
/// Entwurfs, Formatfehler im JSON und Abwärtskompatibilität (ADR 0009, 0013, 0014).
/// </summary>
public sealed class BezugsformGrenzfallApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly object Ring26 = new { art = "ring", durchmesserCm = 26m, hoeheCm = 6m };

    private readonly HttpClient _client = factory.CreateClientMitAktuellemStand();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<Guid> AnlegenAsync(object? bezugsform, bool mitZutaten = true)
    {
        var zutaten = mitZutaten
            ? new[] { new { bezeichnung = "Butter", gramm = 140m }, new { bezeichnung = "Zucker", gramm = 140m }, new { bezeichnung = "Eier", gramm = 280m } }
            : [];
        var antwort = await _client.PostAsJsonAsync("/api/v1/rezepte", new { name = "Sachermasse", bezugsform, zutaten }, _ct);
        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();
    }

    private async Task FreigebenAsync(Guid id) =>
        (await _client.PostAsync($"/api/v1/rezepte/{id}/entwurf/freigabe", content: null, _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

    private async Task<JsonElement> EntwurfAnlegenAsync(Guid id)
    {
        var antwort = await _client.PostAsync($"/api/v1/rezepte/{id}/entwurf", content: null, _ct);
        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct);
    }

    private Task<HttpResponseMessage> BearbeitenAsync(Guid id, object? bezugsform) =>
        _client.PutAsJsonAsync($"/api/v1/rezepte/{id}/entwurf", new { bezugsform, zutaten = new[] { new { bezeichnung = "Butter", gramm = 100m } } }, _ct);

    private Task<HttpResponseMessage> RohBearbeitenAsync(Guid id, string bezugsformJson) =>
        _client.PutAsync(
            $"/api/v1/rezepte/{id}/entwurf",
            new StringContent($$"""{"zutaten":[{"bezeichnung":"Mehl","gramm":100}],"bezugsform":{{bezugsformJson}}}""", Encoding.UTF8, "application/json"),
            _ct);

    private Task<HttpResponseMessage> RohAnlegenAsync(string bezugsformJson) =>
        _client.PostAsync(
            "/api/v1/rezepte",
            new StringContent($$"""{"name":"Probe","zutaten":[{"bezeichnung":"Mehl","gramm":100}],"bezugsform":{{bezugsformJson}}}""", Encoding.UTF8, "application/json"),
            _ct);

    private static async Task<string?> CodeAsync(HttpResponseMessage antwort) =>
        (await antwort.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static IEnumerable<decimal> Gramm(JsonElement element) =>
        element.GetProperty("zutaten").EnumerateArray().Select(z => z.GetProperty("gramm").GetDecimal());

    // ---- Blech-Endpunkt: Formatfehler und Fachregeln ----

    [Theory]
    [InlineData("laengeCm=60,5&breiteCm=40&hoeheCm=2")]
    [InlineData("laengeCm=60&breiteCm=40&hoeheCm=1,5")]
    [InlineData("laengeCm=60&hoeheCm=2")]
    [InlineData("laengeCm=60&breiteCm=40")]
    [InlineData("laengeCm=6e1&breiteCm=40&hoeheCm=2")]
    [InlineData("laengeCm=&breiteCm=40&hoeheCm=2")]
    [InlineData("laengeCm=1.000&breiteCm=40&hoeheCm=2&fassung=abc")]
    [InlineData("laengeCm=60&breiteCm=40&hoeheCm=2&fassung=-1")]
    public async Task Formatfehler_beim_Umrechnen_auf_Blech_liefern_400(string query)
    {
        var id = await AnlegenAsync(Ring26);

        (await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert/blech?{query}", _ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("laengeCm=0.9&breiteCm=40&hoeheCm=2", "form.mass_ungueltig")]
    [InlineData("laengeCm=60&breiteCm=200.1&hoeheCm=2", "form.mass_ungueltig")]
    [InlineData("laengeCm=60&breiteCm=40&hoeheCm=0", "form.hoehe_ungueltig")]
    [InlineData("laengeCm=60&breiteCm=40&hoeheCm=50.1", "form.hoehe_ungueltig")]
    [InlineData("laengeCm=60.55&breiteCm=40&hoeheCm=2", "form.zu_genau")]
    [InlineData("laengeCm=-60&breiteCm=40&hoeheCm=2", "form.mass_ungueltig")]
    public async Task Ungueltiges_Blech_liefert_422_mit_Code(string query, string code)
    {
        var id = await AnlegenAsync(Ring26);

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert/blech?{query}", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(antwort)).ShouldBe(code);
    }

    [Fact]
    public async Task Ohne_Bezugsform_liefert_Umrechnen_auf_Blech_409()
    {
        var id = await AnlegenAsync(bezugsform: null);

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert/blech?laengeCm=60&breiteCm=40&hoeheCm=2", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(antwort)).ShouldBe("fassung.ohne_bezugsform");
    }

    // ---- Grenzwerte der Zielform ----

    [Theory]
    [InlineData("durchmesserCm=1&hoeheCm=0.1")]
    [InlineData("durchmesserCm=200&hoeheCm=50")]
    [InlineData("durchmesserCm=18.50&hoeheCm=6.0")]
    public async Task Zielring_an_den_Grenzen_ist_gueltig(string query)
    {
        var id = await AnlegenAsync(Ring26);

        (await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert/ring?{query}", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("durchmesserCm=0.9&hoeheCm=6", "form.mass_ungueltig")]
    [InlineData("durchmesserCm=200.1&hoeheCm=6", "form.mass_ungueltig")]
    [InlineData("durchmesserCm=18&hoeheCm=0.09", "form.hoehe_ungueltig")]
    [InlineData("durchmesserCm=18&hoeheCm=0", "form.hoehe_ungueltig")]
    [InlineData("durchmesserCm=18&hoeheCm=6.05", "form.zu_genau")]
    public async Task Zielring_knapp_ausserhalb_liefert_422(string query, string code)
    {
        var id = await AnlegenAsync(Ring26);

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert/ring?{query}", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(antwort)).ShouldBe(code);
    }

    // ---- Fachliche Szenarien ----

    [Fact]
    public async Task Sachertorte_von_Ring_26_auf_Ring_18()
    {
        var id = await AnlegenAsync(Ring26);

        var skaliert = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert/ring?durchmesserCm=18&hoeheCm=6", _ct);

        Gramm(skaliert).ShouldBe([67.10m, 67.10m, 134.20m]);
        skaliert.GetProperty("zielgewichtGramm").GetDecimal().ShouldBe(268.40m);
        skaliert.GetProperty("name").GetString().ShouldBe("Sachermasse");
        skaliert.GetProperty("rezeptId").GetGuid().ShouldBe(id);
    }

    [Fact]
    public async Task Umrechnen_veraendert_das_Rezept_nicht()
    {
        var id = await AnlegenAsync(Ring26);
        var vorher = await _client.GetAsync($"/api/v1/rezepte/{id}", _ct);

        await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert/blech?laengeCm=60&breiteCm=40&hoeheCm=2", _ct);

        var nachher = await _client.GetAsync($"/api/v1/rezepte/{id}", _ct);
        (await nachher.Content.ReadAsStringAsync(_ct)).ShouldBe(await vorher.Content.ReadAsStringAsync(_ct));
        nachher.Headers.ETag.ShouldBe(vorher.Headers.ETag);
    }

    [Fact]
    public async Task Mit_Form_aber_ohne_Zutaten_liefert_Umrechnen_409()
    {
        var id = await AnlegenAsync(Ring26, mitZutaten: false);

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert/ring?durchmesserCm=18&hoeheCm=6", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(antwort)).ShouldBe("rezept.ohne_zutaten");
    }

    // ---- Wahl der Fassung ----

    [Fact]
    public async Task Ohne_Fassungsangabe_rechnet_die_massgebliche_Fassung_mit_ihrer_Form()
    {
        var id = await AnlegenAsync(Ring26);
        await FreigebenAsync(id);
        await EntwurfAnlegenAsync(id);
        (await BearbeitenAsync(id, new { art = "ring", durchmesserCm = 13m, hoeheCm = 6m })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var massgeblich = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert/ring?durchmesserCm=13&hoeheCm=6", _ct);
        var entwurf = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert/ring?durchmesserCm=13&hoeheCm=6&fassung=2", _ct);

        massgeblich.GetProperty("fassung").GetInt32().ShouldBe(1);
        massgeblich.GetProperty("faktor").GetDecimal().ShouldBe(0.25m);
        Gramm(massgeblich).ShouldBe([35m, 35m, 70m]);
        entwurf.GetProperty("fassung").GetInt32().ShouldBe(2);
        Math.Abs(entwurf.GetProperty("faktor").GetDecimal() - 1m).ShouldBeLessThan(0.000_000_001m);
        Gramm(entwurf).ShouldBe([100m]);
    }

    [Fact]
    public async Task Gewaehlte_Fassung_ohne_Form_liefert_409_obwohl_die_massgebliche_eine_hat()
    {
        var id = await AnlegenAsync(Ring26);
        await FreigebenAsync(id);
        await EntwurfAnlegenAsync(id);
        (await BearbeitenAsync(id, bezugsform: null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert/ring?durchmesserCm=18&hoeheCm=6&fassung=2", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(antwort)).ShouldBe("fassung.ohne_bezugsform");
        (await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert/ring?durchmesserCm=18&hoeheCm=6", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- Bezugsform im Lebenszyklus der Fassungen ----

    [Fact]
    public async Task Neuer_Entwurf_und_Freigabe_zeigen_die_uebernommene_Form()
    {
        var id = await AnlegenAsync(new { art = "blech", laengeCm = 60m, breiteCm = 40m, hoeheCm = 1.5m });
        var freigabe = await _client.PostAsync($"/api/v1/rezepte/{id}/entwurf/freigabe", content: null, _ct);
        var freigegeben = await freigabe.Content.ReadFromJsonAsync<JsonElement>(_ct);

        var entwurf = await EntwurfAnlegenAsync(id);

        foreach (var form in new[] { freigegeben.GetProperty("bezugsform"), entwurf.GetProperty("bezugsform") })
        {
            form.GetProperty("art").GetString().ShouldBe("blech");
            form.GetProperty("laengeCm").GetDecimal().ShouldBe(60m);
            form.GetProperty("breiteCm").GetDecimal().ShouldBe(40m);
            form.GetProperty("hoeheCm").GetDecimal().ShouldBe(1.5m);
            form.TryGetProperty("durchmesserCm", out _).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Rezeptdetails_zeigen_die_Form_der_massgeblichen_Fassung_nicht_die_des_Entwurfs()
    {
        var id = await AnlegenAsync(Ring26);
        await FreigebenAsync(id);
        await EntwurfAnlegenAsync(id);
        (await BearbeitenAsync(id, new { art = "blech", laengeCm = 60m, breiteCm = 40m, hoeheCm = 2m })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var rezept = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}", _ct);

        rezept.GetProperty("massgeblicheFassung").GetInt32().ShouldBe(1);
        rezept.GetProperty("bezugsform").GetProperty("art").GetString().ShouldBe("ring");
    }

    [Fact]
    public async Task Bearbeiten_mit_ungueltiger_Form_liefert_422_und_aendert_nichts()
    {
        var id = await AnlegenAsync(Ring26);

        var antwort = await BearbeitenAsync(id, new { art = "ring", durchmesserCm = 26m, hoeheCm = 0.05m });

        antwort.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(antwort)).ShouldBe("form.hoehe_ungueltig");
        var fassung = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/fassungen/1", _ct);
        fassung.GetProperty("bezugsform").GetProperty("hoeheCm").GetDecimal().ShouldBe(6m);
        Gramm(fassung).ShouldBe([140m, 140m, 280m]);
    }

    [Fact]
    public async Task Bearbeiten_mit_ungueltiger_Form_ohne_Entwurf_liefert_422_vor_409()
    {
        var id = await AnlegenAsync(Ring26);
        await FreigebenAsync(id);

        var antwort = await BearbeitenAsync(id, new { art = "blech", laengeCm = 300m, breiteCm = 40m, hoeheCm = 2m });

        antwort.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(antwort)).ShouldBe("form.mass_ungueltig");
    }

    [Theory]
    [InlineData("""{"art":"herz","hoeheCm":5}""")]
    [InlineData("""{"art":"blech","breiteCm":40,"hoeheCm":5}""")]
    [InlineData("""{"art":"ring","durchmesserCm":26,"hoeheCm":"6"}""")]
    public async Task Bearbeiten_mit_unvollstaendiger_Form_liefert_400(string bezugsform)
    {
        var id = await AnlegenAsync(Ring26);

        (await RohBearbeitenAsync(id, bezugsform)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- Formatfehler im JSON beim Anlegen ----

    [Theory]
    [InlineData("""{"art":"ring","durchmesserCm":26,"hoeheCm":null}""")]
    [InlineData("""{"art":"ring","durchmesserCm":null,"hoeheCm":6}""")]
    [InlineData("""{"art":"Ring","durchmesserCm":26,"hoeheCm":6}""")]
    [InlineData("""{"art":null,"durchmesserCm":26,"hoeheCm":6}""")]
    [InlineData("""{"art":"","durchmesserCm":26,"hoeheCm":6}""")]
    [InlineData("\"ring\"")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"art":"blech","laengeCm":60,"breiteCm":"40,5","hoeheCm":2}""")]
    public async Task Formatfehler_in_der_Bezugsform_liefern_400(string bezugsform) =>
        (await RohAnlegenAsync(bezugsform)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Theory]
    [InlineData("""{"art":"ring","durchmesserCm":18.55,"hoeheCm":6}""", "form.zu_genau")]
    [InlineData("""{"art":"ring","durchmesserCm":26,"hoeheCm":0}""", "form.hoehe_ungueltig")]
    [InlineData("""{"art":"blech","laengeCm":60,"breiteCm":0.5,"hoeheCm":2}""", "form.mass_ungueltig")]
    public async Task Ungueltige_Masse_beim_Anlegen_liefern_422_mit_Code(string bezugsform, string code)
    {
        var antwort = await RohAnlegenAsync(bezugsform);

        antwort.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(antwort)).ShouldBe(code);
    }

    [Fact]
    public async Task Grenzwerte_der_Bezugsform_beim_Anlegen_werden_angenommen()
    {
        var antwort = await RohAnlegenAsync("""{"art":"blech","laengeCm":200,"breiteCm":1,"hoeheCm":0.1}""");

        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
        var form = (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("bezugsform");
        form.GetProperty("laengeCm").GetDecimal().ShouldBe(200m);
        form.GetProperty("breiteCm").GetDecimal().ShouldBe(1m);
        form.GetProperty("hoeheCm").GetDecimal().ShouldBe(0.1m);
    }

    // ---- Abwärtskompatibilität ----

    [Fact]
    public async Task Bisheriger_Client_legt_ohne_bezugsform_an_und_bearbeitet_ohne_bezugsform()
    {
        var angelegt = await _client.PostAsync(
            "/api/v1/rezepte",
            new StringContent("""{"name":"Vanillecreme","zutaten":[{"bezeichnung":"Milch","gramm":500}]}""", Encoding.UTF8, "application/json"),
            _ct);
        angelegt.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await angelegt.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();

        var bearbeitet = await _client.PutAsync(
            $"/api/v1/rezepte/{id}/entwurf",
            new StringContent("""{"zutaten":[{"bezeichnung":"Milch","gramm":400}]}""", Encoding.UTF8, "application/json"),
            _ct);

        bearbeitet.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fassung = await bearbeitet.Content.ReadFromJsonAsync<JsonElement>(_ct);
        fassung.GetProperty("bezugsform").ValueKind.ShouldBe(JsonValueKind.Null);
        fassung.GetProperty("gesamtgewichtGramm").GetDecimal().ShouldBe(400m);
    }

    [Fact]
    public async Task Skalieren_nach_Gewicht_liefert_weiterhin_alle_bisherigen_Felder()
    {
        var id = await AnlegenAsync(Ring26);

        var skaliert = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=280", _ct);

        skaliert.GetProperty("rezeptId").GetGuid().ShouldBe(id);
        skaliert.GetProperty("name").GetString().ShouldBe("Sachermasse");
        skaliert.GetProperty("fassung").GetInt32().ShouldBe(1);
        skaliert.GetProperty("zielgewichtGramm").GetDecimal().ShouldBe(280m);
        Gramm(skaliert).ShouldBe([70m, 70m, 140m]);
        skaliert.GetProperty("faktor").GetDecimal().ShouldBe(0.5m);
        skaliert.GetProperty("zielform").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
