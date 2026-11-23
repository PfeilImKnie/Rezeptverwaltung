using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rezeptverwaltung.Api.Tests;

public sealed class RezepteApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static readonly object Muerbeteig = new
    {
        name = "Mürbeteig",
        zutaten = new object[]
        {
            new { bezeichnung = "Zucker", gramm = 100 },
            new { bezeichnung = "Butter", gramm = 200 },
            new { bezeichnung = "Mehl", gramm = 300 },
        },
    };

    private async Task<JsonElement> AnlegenAsync(object rezept)
    {
        var antwort = await _client.PostAsJsonAsync("/api/v1/rezepte", rezept, _ct);
        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct);
    }

    [Fact]
    public async Task Anlegen_liefert_201_mit_Location_und_Rezept()
    {
        var antwort = await _client.PostAsJsonAsync("/api/v1/rezepte", Muerbeteig, _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct);
        var id = body.GetProperty("id").GetGuid();
        antwort.Headers.Location!.ToString().ShouldBe($"/api/v1/rezepte/{id}");
        body.GetProperty("name").GetString().ShouldBe("Mürbeteig");
        body.GetProperty("gesamtgewichtGramm").GetDecimal().ShouldBe(600m);
        body.GetProperty("zutaten").GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task Angelegtes_Rezept_ist_abrufbar()
    {
        var angelegt = await AnlegenAsync(Muerbeteig);
        var id = angelegt.GetProperty("id").GetGuid();

        var abgerufen = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}", _ct);

        abgerufen.GetProperty("name").GetString().ShouldBe("Mürbeteig");
        abgerufen.GetProperty("zutaten")[1].GetProperty("bezeichnung").GetString().ShouldBe("Butter");
    }

    [Fact]
    public async Task Unbekanntes_Rezept_liefert_404()
    {
        var antwort = await _client.GetAsync($"/api/v1/rezepte/{Guid.NewGuid()}", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Skalieren_liefert_umgerechnete_Mengen()
    {
        var id = (await AnlegenAsync(Muerbeteig)).GetProperty("id").GetGuid();

        var skaliert = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=900", _ct);

        skaliert.GetProperty("zielgewichtGramm").GetDecimal().ShouldBe(900m);
        skaliert.GetProperty("zutaten").EnumerateArray().Select(z => z.GetProperty("gramm").GetDecimal())
            .ShouldBe([150m, 300m, 450m]);
    }

    [Fact]
    public async Task Skalieren_unbekanntes_Rezept_liefert_404()
    {
        var antwort = await _client.GetAsync($"/api/v1/rezepte/{Guid.NewGuid()}/skaliert?zielgewichtGramm=100", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Skalieren_ohne_Zielgewicht_liefert_400()
    {
        var id = (await AnlegenAsync(Muerbeteig)).GetProperty("id").GetGuid();

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Fachregelverletzung_liefert_422_Problem_Details_mit_Code()
    {
        var antwort = await _client.PostAsJsonAsync(
            "/api/v1/rezepte",
            new { name = "   ", zutaten = Array.Empty<object>() },
            _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        antwort.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct);
        problem.GetProperty("code").GetString().ShouldBe("rezept.name_leer");
        problem.GetProperty("status").GetInt32().ShouldBe(422);
    }

    [Theory]
    [InlineData(0x0085)] // Schemathesis-Fund: galt im Vertrag als sichtbar, .NET-Trim entfernte es
    [InlineData(0x001C)] // Review-Fund: Leerraum für Python, nicht für ECMAScript
    [InlineData(0xFEFF)] // Review-Fund: Leerraum für ECMAScript, nicht für Python
    public async Task Bezeichnung_nur_aus_Leerraum_liefert_422(int codepoint)
    {
        var antwort = await _client.PostAsJsonAsync(
            "/api/v1/rezepte",
            new { name = "Test", zutaten = new[] { new { bezeichnung = new string((char)codepoint, 1), gramm = 1 } } },
            _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("code").GetString().ShouldBe("zutat.bezeichnung_leer");
    }

    [Theory]
    [InlineData(200, HttpStatusCode.Created)]
    [InlineData(201, HttpStatusCode.UnprocessableEntity)]
    public async Task Namenslaenge_wird_in_Codepoints_gezaehlt(int anzahlEmoji, HttpStatusCode erwartet)
    {
        var name = string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x1F382), anzahlEmoji));

        var antwort = await _client.PostAsJsonAsync("/api/v1/rezepte", new { name, zutaten = Array.Empty<object>() }, _ct);

        antwort.StatusCode.ShouldBe(erwartet);
    }

    [Fact]
    public async Task Fehlendes_Pflichtfeld_liefert_400()
    {
        var antwort = await _client.PostAsJsonAsync("/api/v1/rezepte", new { name = "Ohne Zutatenliste" }, _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Null_als_Pflichtfeld_liefert_400()
    {
        var antwort = await _client.PostAsJsonAsync(
            "/api/v1/rezepte",
            new { name = (string?)null, zutaten = Array.Empty<object>() },
            _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Health_Endpoint_meldet_gesund()
    {
        var antwort = await _client.GetAsync("/health", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
