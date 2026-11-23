using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>Bezugsform und Umrechnen auf Ring/Blech Ende-zu-Ende (ADR 0014).</summary>
public sealed class BezugsformApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClientMitAktuellemStand();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<Guid> AnlegenAsync(object? bezugsform)
    {
        var antwort = await _client.PostAsJsonAsync(
            "/api/v1/rezepte",
            new
            {
                name = "Sachermasse",
                bezugsform,
                zutaten = new[] { new { bezeichnung = "Butter", gramm = 140m }, new { bezeichnung = "Zucker", gramm = 140m }, new { bezeichnung = "Eier", gramm = 280m } },
            },
            _ct);
        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> RohAnlegenAsync(string bezugsformJson) =>
        _client.PostAsync(
            "/api/v1/rezepte",
            new StringContent($$"""{"name":"Probe","zutaten":[{"bezeichnung":"Mehl","gramm":100}],"bezugsform":{{bezugsformJson}}}""", Encoding.UTF8, "application/json"),
            _ct);

    private static async Task<string?> CodeAsync(HttpResponseMessage antwort) =>
        (await antwort.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Rezept_mit_Ring_zeigt_die_Bezugsform_ohne_unpassende_Felder()
    {
        var id = await AnlegenAsync(new { art = "ring", durchmesserCm = 26m, hoeheCm = 6m });

        var form = (await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}", _ct)).GetProperty("bezugsform");

        form.GetProperty("art").GetString().ShouldBe("ring");
        form.GetProperty("durchmesserCm").GetDecimal().ShouldBe(26m);
        form.GetProperty("hoeheCm").GetDecimal().ShouldBe(6m);
        form.TryGetProperty("laengeCm", out _).ShouldBeFalse();
        form.TryGetProperty("breiteCm", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Rezept_ohne_Form_hat_bezugsform_null()
    {
        var id = await AnlegenAsync(bezugsform: null);

        (await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}", _ct)).GetProperty("bezugsform").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Umrechnen_auf_halben_Durchmesser_ergibt_ein_Viertel()
    {
        var id = await AnlegenAsync(new { art = "ring", durchmesserCm = 26m, hoeheCm = 6m });

        var skaliert = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert/ring?durchmesserCm=13&hoeheCm=6", _ct);

        skaliert.GetProperty("faktor").GetDecimal().ShouldBe(0.25m);
        skaliert.GetProperty("zielgewichtGramm").GetDecimal().ShouldBe(140m);
        skaliert.GetProperty("zielform").GetProperty("durchmesserCm").GetDecimal().ShouldBe(13m);
        skaliert.GetProperty("zutaten").EnumerateArray().Select(z => z.GetProperty("gramm").GetDecimal()).ShouldBe([35m, 35m, 70m]);
    }

    [Fact]
    public async Task Sachertorte_von_Ring_26_auf_Blech_umrechnen()
    {
        var id = await AnlegenAsync(new { art = "ring", durchmesserCm = 26m, hoeheCm = 6m });

        var skaliert = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert/blech?laengeCm=60&breiteCm=40&hoeheCm=1.5", _ct);

        // 60 × 40 × 1,5 = 3600 cm³ ; π × 13² × 6 = 3185,575 cm³
        Math.Abs(skaliert.GetProperty("faktor").GetDecimal() - 1.1300943m).ShouldBeLessThan(0.000_001m);
        skaliert.GetProperty("zielform").GetProperty("art").GetString().ShouldBe("blech");
        skaliert.GetProperty("zutaten")[0].GetProperty("gramm").GetDecimal().ShouldBe(158.21m);
    }

    [Fact]
    public async Task Faktor_wird_ohne_Rechenrauschen_ausgegeben()
    {
        var id = await AnlegenAsync(new { art = "ring", durchmesserCm = 13m, hoeheCm = 6m });

        var skaliert = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert/ring?durchmesserCm=26&hoeheCm=6", _ct);

        skaliert.GetProperty("faktor").GetRawText().ShouldBe("4");
        skaliert.GetProperty("zutaten")[0].GetProperty("gramm").GetDecimal().ShouldBe(560m);
    }

    [Fact]
    public async Task Sehr_kleiner_Faktor_erscheint_nie_als_0()
    {
        // Review-Befund: Rundung auf Nachkommastellen hätte 1e-7 als 0 ausgegeben (Vertrag: faktor > 0).
        var antwort = await _client.PostAsJsonAsync(
            "/api/v1/rezepte",
            new { name = "Großansatz", bezugsform = new { art = "blech", laengeCm = 200m, breiteCm = 200m, hoeheCm = 50m }, zutaten = new[] { new { bezeichnung = "Mehl", gramm = 100_000m } } },
            _ct);
        var id = (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();

        var nachGewicht = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=0.01", _ct);
        var aufForm = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert/ring?durchmesserCm=1&hoeheCm=0.1", _ct);

        nachGewicht.GetProperty("faktor").GetDecimal().ShouldBe(0.0000001m);
        aufForm.GetProperty("faktor").GetDecimal().ShouldBeGreaterThan(0m);
        Math.Abs(aufForm.GetProperty("faktor").GetDecimal() - 0.0000000392699081699m).ShouldBeLessThan(0.000_000_000_000_000_1m);
    }

    [Fact]
    public async Task Skalieren_nach_Gewicht_meldet_den_Faktor_und_keine_Zielform()
    {
        var id = await AnlegenAsync(bezugsform: null);

        var skaliert = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=1120", _ct);

        skaliert.GetProperty("faktor").GetDecimal().ShouldBe(2m);
        skaliert.GetProperty("zielform").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Ohne_Bezugsform_liefert_Umrechnen_auf_Form_409()
    {
        var id = await AnlegenAsync(bezugsform: null);

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert/ring?durchmesserCm=18&hoeheCm=6", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(antwort)).ShouldBe("fassung.ohne_bezugsform");
    }

    [Theory]
    [InlineData("durchmesserCm=0.5&hoeheCm=6", "form.mass_ungueltig")]
    [InlineData("durchmesserCm=18&hoeheCm=51", "form.hoehe_ungueltig")]
    [InlineData("durchmesserCm=18.05&hoeheCm=6", "form.zu_genau")]
    public async Task Ungueltige_Zielform_liefert_422_auch_vor_fehlender_Bezugsform(string query, string code)
    {
        var id = await AnlegenAsync(bezugsform: null);

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert/ring?{query}", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(antwort)).ShouldBe(code);
    }

    [Theory]
    [InlineData("durchmesserCm=18,5&hoeheCm=6")]
    [InlineData("hoeheCm=6")]
    [InlineData("durchmesserCm=18")]
    [InlineData("durchmesserCm=achtzehn&hoeheCm=6")]
    [InlineData("durchmesserCm=18&hoeheCm=6&fassung=0")]
    public async Task Formatfehler_beim_Umrechnen_liefern_400(string query)
    {
        var id = await AnlegenAsync(new { art = "ring", durchmesserCm = 26m, hoeheCm = 6m });

        (await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert/ring?{query}", _ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unbekanntes_Rezept_oder_unbekannte_Fassung_liefern_404()
    {
        var id = await AnlegenAsync(new { art = "ring", durchmesserCm = 26m, hoeheCm = 6m });

        (await _client.GetAsync($"/api/v1/rezepte/{Guid.NewGuid()}/skaliert/ring?durchmesserCm=18&hoeheCm=6", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert/blech?laengeCm=60&breiteCm=40&hoeheCm=2&fassung=9", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("""{"art":"herz","hoeheCm":5}""")]
    [InlineData("""{"art":"ring","hoeheCm":5}""")]
    [InlineData("""{"art":"blech","laengeCm":60,"hoeheCm":5}""")]
    [InlineData("""{"art":"ring","durchmesserCm":26}""")]
    [InlineData("""{"durchmesserCm":26,"hoeheCm":5}""")]
    [InlineData("""{"art":"ring","durchmesserCm":"26","hoeheCm":5}""")]
    public async Task Unvollstaendige_oder_unbekannte_Form_liefert_400(string bezugsform) =>
        (await RohAnlegenAsync(bezugsform)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Ungueltiges_Formmass_beim_Anlegen_liefert_422() =>
        (await CodeAsync(await RohAnlegenAsync("""{"art":"ring","durchmesserCm":300,"hoeheCm":5}"""))).ShouldBe("form.mass_ungueltig");

    [Fact]
    public async Task Unpassende_Zusatzmasse_werden_ignoriert()
    {
        var antwort = await RohAnlegenAsync("""{"art":"ring","durchmesserCm":26,"hoeheCm":6,"laengeCm":60,"breiteCm":40}""");

        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
        var form = (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("bezugsform");
        form.TryGetProperty("laengeCm", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Entwurf_bekommt_eine_andere_Form_die_freigegebene_Fassung_behaelt_ihre()
    {
        var id = await AnlegenAsync(new { art = "ring", durchmesserCm = 26m, hoeheCm = 6m });
        await _client.PostAsync($"/api/v1/rezepte/{id}/entwurf/freigabe", content: null, _ct);
        await _client.PostAsync($"/api/v1/rezepte/{id}/entwurf", content: null, _ct);

        var bearbeitet = await _client.PutAsJsonAsync(
            $"/api/v1/rezepte/{id}/entwurf",
            new { bezugsform = new { art = "blech", laengeCm = 60m, breiteCm = 40m, hoeheCm = 2m }, zutaten = new[] { new { bezeichnung = "Butter", gramm = 600m } } },
            _ct);

        bearbeitet.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await bearbeitet.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("bezugsform").GetProperty("art").GetString().ShouldBe("blech");
        var fassung1 = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/fassungen/1", _ct);
        fassung1.GetProperty("bezugsform").GetProperty("art").GetString().ShouldBe("ring");
        var entwurf = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert/ring?durchmesserCm=26&hoeheCm=6&fassung=2", _ct);
        entwurf.GetProperty("fassung").GetInt32().ShouldBe(2);
    }
}
