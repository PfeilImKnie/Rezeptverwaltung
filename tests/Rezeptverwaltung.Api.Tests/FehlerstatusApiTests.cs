using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>
/// Unabhängiger Test der Fehlerstatus (400 / 404 / 409 / 422) aus api/openapi.yaml.
/// Leitlinie: Keine Anfrage, ob vertragsgemäß oder nicht, darf einen 500 auslösen.
/// </summary>
public sealed class FehlerstatusApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<HttpResponseMessage> RohPostAsync(string json) =>
        await _client.PostAsync("/api/v1/rezepte", new StringContent(json, Encoding.UTF8, "application/json"), _ct);

    private async Task<Guid> AnlegenAsync(object rezept)
    {
        var antwort = await _client.PostAsJsonAsync("/api/v1/rezepte", rezept, _ct);
        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();
    }

    private Task<Guid> LeeresRezeptAnlegenAsync() => AnlegenAsync(new { name = "Noch leer", zutaten = Array.Empty<object>() });

    private Task<Guid> SachertorteAnlegenAsync() => AnlegenAsync(new
    {
        name = "Sachermasse Ø 26",
        zutaten = new object[]
        {
            new { bezeichnung = "Butter", gramm = 140 },
            new { bezeichnung = "Staubzucker", gramm = 30 },
            new { bezeichnung = "Eigelb", gramm = 120 },
            new { bezeichnung = "Kuvertüre", gramm = 140 },
            new { bezeichnung = "Eiklar", gramm = 180 },
            new { bezeichnung = "Kristallzucker", gramm = 110 },
            new { bezeichnung = "Mehl", gramm = 140 },
        },
    });

    private async Task<JsonElement> ProblemMitStatusAsync(HttpResponseMessage antwort, HttpStatusCode erwartet)
    {
        antwort.StatusCode.ShouldBe(erwartet);
        antwort.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct);
        problem.GetProperty("status").GetInt32().ShouldBe((int)erwartet);
        return problem;
    }

    private async Task FachregelAsync(HttpResponseMessage antwort, HttpStatusCode erwartet, string code)
    {
        var problem = await ProblemMitStatusAsync(antwort, erwartet);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    // --- (1) null-Elemente und andere Formatfehler beim Anlegen: 400 Problem Details ---

    [Theory]
    [InlineData("""{"name":"0","zutaten":[null]}""")]
    [InlineData("""{"name":"Biskuit","zutaten":[{"bezeichnung":"Mehl","gramm":100},null]}""")]
    [InlineData("""{"name":"Biskuit","zutaten":[null,{"bezeichnung":"Mehl","gramm":100}]}""")]
    [InlineData("""{"name":"Biskuit","zutaten":[null,null,null]}""")]
    public async Task Null_Element_in_der_Zutatenliste_liefert_400_Problem_Details(string json) =>
        await ProblemMitStatusAsync(await RohPostAsync(json), HttpStatusCode.BadRequest);

    [Theory]
    [InlineData("""{"name":"Biskuit","zutaten":null}""")]
    [InlineData("""{"zutaten":[]}""")]
    [InlineData("""{"name":"Biskuit","zutaten":[{"bezeichnung":null,"gramm":1}]}""")]
    [InlineData("""{"name":"Biskuit","zutaten":[{"gramm":1}]}""")]
    [InlineData("""{"name":"Biskuit","zutaten":[{"bezeichnung":"Mehl"}]}""")]
    [InlineData("""{"name":"Biskuit","zutaten":[{"bezeichnung":"Mehl","gramm":null}]}""")]
    [InlineData("""{"name":"Biskuit","zutaten":[{"bezeichnung":"Mehl","gramm":true}]}""")]
    [InlineData("""{"name":"Biskuit","zutaten":[{"bezeichnung":"Mehl","gramm":1e400}]}""")]
    [InlineData("""{"name":"Biskuit","zutaten":[{"bezeichnung":"Mehl","gramm":79228162514264337593543950336}]}""")]
    [InlineData("""{"name":"Biskuit","zutaten":[1]}""")]
    [InlineData("""{"name":"Biskuit","zutaten":[[]]}""")]
    [InlineData("""{"name":"Biskuit","zutaten":{}}""")]
    [InlineData("""{"name":42,"zutaten":[]}""")]
    [InlineData("""null""")]
    [InlineData("""[]""")]
    [InlineData("""{""")]
    [InlineData("")]
    public async Task Syntaktisch_ungueltiger_Body_liefert_400_Problem_Details(string json) =>
        await ProblemMitStatusAsync(await RohPostAsync(json), HttpStatusCode.BadRequest);

    /// <summary>Vertrag: gramm ist type number. Ein JSON-String ist ein Formatfehler (400), kein gültiges Gewicht.</summary>
    [Fact]
    public async Task Gramm_als_Zeichenkette_wird_als_Formatfehler_abgewiesen() =>
        await ProblemMitStatusAsync(
            await RohPostAsync("""{"name":"Biskuit","zutaten":[{"bezeichnung":"Mehl","gramm":"100"}]}"""),
            HttpStatusCode.BadRequest);

    [Fact]
    public async Task Leere_Zutatenliste_ist_vertragsgemaess_und_liefert_201()
    {
        var id = await LeeresRezeptAnlegenAsync();

        var rezept = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}", _ct);

        rezept.GetProperty("zutaten").GetArrayLength().ShouldBe(0);
        rezept.GetProperty("gesamtgewichtGramm").GetDecimal().ShouldBe(0m);
    }

    // --- (3) Ungültige Eingaben beim Anlegen: 422 mit stabilem code ---

    [Theory]
    [InlineData("0", "zutat.menge_null")]
    [InlineData("0.005", "gewicht.zu_genau")]
    [InlineData("0.001", "gewicht.zu_genau")]
    [InlineData("100.001", "gewicht.zu_genau")]
    [InlineData("-0.01", "gewicht.negativ")]
    [InlineData("-1", "gewicht.negativ")]
    [InlineData("1000000000.01", "gewicht.zu_gross")]
    [InlineData("1e-20", "gewicht.zu_genau")]
    [InlineData("1e-30", "zutat.menge_null")] // jenseits der decimal-Genauigkeit, wird beim Einlesen zu 0
    public async Task Ungueltige_Zutatenmenge_liefert_422_mit_Code(string gramm, string code) =>
        await FachregelAsync(
            await RohPostAsync($$"""{"name":"Biskuit","zutaten":[{"bezeichnung":"Mehl","gramm":{{gramm}}}]}"""),
            HttpStatusCode.UnprocessableEntity,
            code);

    [Theory]
    [InlineData("0.01")]
    [InlineData("1000000000")]
    [InlineData("999999999.99")]
    public async Task Zutatenmenge_an_der_Grenze_wird_angenommen(string gramm)
    {
        var antwort = await RohPostAsync($$"""{"name":"Biskuit","zutaten":[{"bezeichnung":"Mehl","gramm":{{gramm}}}]}""");

        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct);
        body.GetProperty("gesamtgewichtGramm").GetDecimal().ShouldBe(decimal.Parse(gramm, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Leere_Zutatenbezeichnung_liefert_422_mit_Code() =>
        await FachregelAsync(
            await RohPostAsync("""{"name":"Biskuit","zutaten":[{"bezeichnung":"   ","gramm":10}]}"""),
            HttpStatusCode.UnprocessableEntity,
            "zutat.bezeichnung_leer");

    [Fact]
    public async Task Zu_langer_Rezeptname_liefert_422_mit_Code() =>
        await FachregelAsync(
            await _client.PostAsJsonAsync("/api/v1/rezepte", new { name = new string('S', 201), zutaten = Array.Empty<object>() }, _ct),
            HttpStatusCode.UnprocessableEntity,
            "rezept.name_zu_lang");

    // --- (2) Skalieren eines Rezepts ohne Zutaten: 409 ---

    [Theory]
    [InlineData("0.01")]
    [InlineData("100")]
    [InlineData("1000000000")]
    public async Task Skalieren_ohne_Zutaten_liefert_409_fuer_jedes_gueltige_Zielgewicht(string ziel)
    {
        var id = await LeeresRezeptAnlegenAsync();

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm={ziel}", _ct);

        await FachregelAsync(antwort, HttpStatusCode.Conflict, "rezept.ohne_zutaten");
    }

    [Fact]
    public async Task Problem_Details_bei_409_haben_Titel_und_Typ_mit_Code()
    {
        var id = await LeeresRezeptAnlegenAsync();

        var problem = await ProblemMitStatusAsync(
            await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=100", _ct),
            HttpStatusCode.Conflict);

        problem.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("type").GetString()!.ShouldEndWith("rezept.ohne_zutaten");
    }

    [Fact]
    public async Task Skalieren_ohne_Zutaten_laesst_das_Rezept_unveraendert()
    {
        var id = await LeeresRezeptAnlegenAsync();

        _ = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=100", _ct);
        var rezept = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}", _ct);

        rezept.GetProperty("zutaten").GetArrayLength().ShouldBe(0);
        rezept.GetProperty("gesamtgewichtGramm").GetDecimal().ShouldBe(0m);
    }

    /// <summary>
    /// 409 ist laut Vertrag nur zulässig, wenn die Anfrage gültig ist. Ein ungültiges Zielgewicht
    /// bleibt eine ungültige Eingabe (422), auch wenn das Rezept zusätzlich keine Zutaten hat.
    /// </summary>
    [Theory]
    [InlineData("0", "skalierung.ziel_null")]
    [InlineData("0.00", "skalierung.ziel_null")]
    [InlineData("0.005", "gewicht.zu_genau")]
    [InlineData("-1", "gewicht.negativ")]
    [InlineData("1000000000.01", "gewicht.zu_gross")]
    public async Task Ungueltiges_Zielgewicht_hat_Vorrang_vor_Zustandskonflikt_und_liefert_422(string ziel, string code)
    {
        var id = await LeeresRezeptAnlegenAsync();

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm={ziel}", _ct);

        await FachregelAsync(antwort, HttpStatusCode.UnprocessableEntity, code);
    }

    // --- (3) Ungültiges Zielgewicht bei einem Rezept mit Zutaten: 422 mit stabilem code ---

    [Theory]
    [InlineData("0", "skalierung.ziel_null")]
    [InlineData("0.00", "skalierung.ziel_null")]
    [InlineData("0.005", "gewicht.zu_genau")]
    [InlineData("0.001", "gewicht.zu_genau")]
    [InlineData("900.001", "gewicht.zu_genau")]
    [InlineData("-0.01", "gewicht.negativ")]
    [InlineData("-900", "gewicht.negativ")]
    [InlineData("1000000000.01", "gewicht.zu_gross")]
    [InlineData("99999999999", "gewicht.zu_gross")]
    public async Task Ungueltiges_Zielgewicht_liefert_422_mit_Code(string ziel, string code)
    {
        var id = await SachertorteAnlegenAsync();

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm={ziel}", _ct);

        await FachregelAsync(antwort, HttpStatusCode.UnprocessableEntity, code);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("1000000000")]
    public async Task Zielgewicht_an_der_Grenze_wird_angenommen(string ziel)
    {
        var id = await SachertorteAnlegenAsync();

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm={ziel}", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Backstube: Sachertorte von Ø 26 cm auf Ø 18 cm, Fläche (18/26)² ≈ 0,4793, also 860 g → 412,19 g.</summary>
    [Fact]
    public async Task Sachertorte_von_26_auf_18_cm_ergibt_stimmige_Mengen()
    {
        var id = await SachertorteAnlegenAsync();

        var skaliert = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=412.19", _ct);

        var mengen = skaliert.GetProperty("zutaten").EnumerateArray().Select(z => z.GetProperty("gramm").GetDecimal()).ToList();
        mengen.Count.ShouldBe(7);
        mengen.ShouldAllBe(g => g > 0m && decimal.Round(g, 2) == g);
        Math.Abs(mengen.Sum() - 412.19m).ShouldBeLessThanOrEqualTo(0.01m * mengen.Count / 2);
        // Butter: 140 / 860 * 412,19 = 67,1007 → 67,10
        mengen[0].ShouldBe(67.10m);
    }

    // --- (5) Kein 500, egal welche Anfrage ---

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e400")]
    [InlineData("1e2")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("")]
    [InlineData("%00")]
    [InlineData("0x10")]
    public async Task Unsinniges_Zielgewicht_liefert_4xx_Problem_Details_statt_500(string ziel)
    {
        var id = await SachertorteAnlegenAsync();

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm={ziel}", _ct);

        ((int)antwort.StatusCode).ShouldBeOneOf(400, 422);
        antwort.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    /// <summary>
    /// Backstube: „1,5“ ist in Deutschland 1,5 g. Es darf auf keinen Fall still als 15 g verstanden werden
    /// (Faktor 10 in der Rezeptur). Laut Vertrag ist „1,5“ keine Zahl, also 400.
    /// </summary>
    [Theory]
    [InlineData("1,5")]
    [InlineData("1,000")]
    public async Task Zielgewicht_mit_Komma_wird_nicht_als_Tausendertrennzeichen_gelesen(string ziel)
    {
        var id = await SachertorteAnlegenAsync();

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm={ziel}", _ct);

        await ProblemMitStatusAsync(antwort, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Doppeltes_Zielgewicht_im_Query_liefert_400()
    {
        // Mehrfachwerte werden als "100,200" gebunden; früher las decimal daraus 100200 g (Review-Befund).
        var id = await SachertorteAnlegenAsync();

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=100&zielgewichtGramm=200", _ct);

        await ProblemMitStatusAsync(antwort, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/v1/rezepte/keine-uuid")]
    [InlineData("/api/v1/rezepte/keine-uuid/skaliert?zielgewichtGramm=100")]
    [InlineData("/api/v1/rezepte/00000000-0000-0000-0000-000000000000")]
    [InlineData("/api/v1/rezepte/00000000-0000-0000-0000-000000000000/skaliert?zielgewichtGramm=100")]
    public async Task Ungueltige_oder_unbekannte_Id_liefert_404(string pfad)
    {
        var antwort = await _client.GetAsync(pfad, _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unbekanntes_Rezept_mit_ungueltigem_Zielgewicht_liefert_kein_500()
    {
        var antwort = await _client.GetAsync($"/api/v1/rezepte/{Guid.NewGuid()}/skaliert?zielgewichtGramm=0.005", _ct);

        ((int)antwort.StatusCode).ShouldBeOneOf(404, 422);
    }

    [Fact]
    public async Task Body_ohne_JSON_Content_Type_liefert_kein_500()
    {
        var antwort = await _client.PostAsync("/api/v1/rezepte", new StringContent("""{"name":"X","zutaten":[]}""", Encoding.UTF8, "text/plain"), _ct);

        ((int)antwort.StatusCode).ShouldBeLessThan(500);
    }
}
