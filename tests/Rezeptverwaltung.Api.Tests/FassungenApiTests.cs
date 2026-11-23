using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>Arbeitsablauf mit Fassungen Ende-zu-Ende über die API und eine echte Datenbank (ADR 0013).</summary>
public sealed class FassungenApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClientMitAktuellemStand();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<Guid> AnlegenAsync(params (string Bezeichnung, decimal Gramm)[] zutaten)
    {
        var antwort = await _client.PostAsJsonAsync(
            "/api/v1/rezepte",
            new { name = "Wiener Masse", zutaten = zutaten.Select(z => new { bezeichnung = z.Bezeichnung, gramm = z.Gramm }) },
            _ct);
        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> RezeptAsync(Guid id) => await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}", _ct);

    private async Task<HttpResponseMessage> FreigebenAsync(Guid id) =>
        await _client.PostAsync($"/api/v1/rezepte/{id}/entwurf/freigabe", content: null, _ct);

    private static async Task<string?> CodeAsync(HttpResponseMessage antwort) =>
        (await antwort.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Neues_Rezept_zeigt_Entwurf_1_als_massgebliche_Fassung()
    {
        var rezept = await RezeptAsync(await AnlegenAsync(("Eier", 300)));

        rezept.GetProperty("massgeblicheFassung").GetInt32().ShouldBe(1);
        rezept.GetProperty("aktuelleFassung").ValueKind.ShouldBe(JsonValueKind.Null);
        rezept.GetProperty("entwurf").GetInt32().ShouldBe(1);
        var fassung = rezept.GetProperty("fassungen")[0];
        fassung.GetProperty("status").GetString().ShouldBe("entwurf");
        fassung.GetProperty("freigegebenAm").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Kompletter_Arbeitsablauf_mit_neuer_Fassung()
    {
        var id = await AnlegenAsync(("Eier", 300), ("Zucker", 150));
        (await FreigebenAsync(id)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var angelegt = await _client.PostAsync($"/api/v1/rezepte/{id}/entwurf", content: null, _ct);
        angelegt.StatusCode.ShouldBe(HttpStatusCode.Created);
        angelegt.Headers.Location!.ToString().ShouldBe($"/api/v1/rezepte/{id}/fassungen/2");

        var bearbeitet = await _client.PutAsJsonAsync(
            $"/api/v1/rezepte/{id}/entwurf",
            new { zutaten = new[] { new { bezeichnung = "Eier", gramm = 200m }, new { bezeichnung = "Zucker", gramm = 200m } } },
            _ct);
        bearbeitet.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Solange der Entwurf nicht freigegeben ist, gilt Fassung 1.
        (await RezeptAsync(id)).GetProperty("gesamtgewichtGramm").GetDecimal().ShouldBe(450m);
        var standard = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=900", _ct);
        standard.GetProperty("fassung").GetInt32().ShouldBe(1);
        var probe = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=900&fassung=2", _ct);
        probe.GetProperty("fassung").GetInt32().ShouldBe(2);
        probe.GetProperty("zutaten")[0].GetProperty("gramm").GetDecimal().ShouldBe(450m);

        var freigabe = await FreigebenAsync(id);
        freigabe.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await freigabe.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("status").GetString().ShouldBe("freigegeben");

        var rezept = await RezeptAsync(id);
        rezept.GetProperty("massgeblicheFassung").GetInt32().ShouldBe(2);
        rezept.GetProperty("aktuelleFassung").GetInt32().ShouldBe(2);
        rezept.GetProperty("entwurf").ValueKind.ShouldBe(JsonValueKind.Null);
        rezept.GetProperty("fassungen").GetArrayLength().ShouldBe(2);

        // Fassung 1 bleibt unverändert abrufbar.
        var fassung1 = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/fassungen/1", _ct);
        fassung1.GetProperty("gesamtgewichtGramm").GetDecimal().ShouldBe(450m);
        fassung1.GetProperty("status").GetString().ShouldBe("freigegeben");
    }

    [Fact]
    public async Task Entwurf_verwerfen_liefert_204()
    {
        var id = await AnlegenAsync(("Eier", 300));
        await FreigebenAsync(id);
        await _client.PostAsync($"/api/v1/rezepte/{id}/entwurf", content: null, _ct);

        var antwort = await _client.DeleteAsync($"/api/v1/rezepte/{id}/entwurf", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RezeptAsync(id)).GetProperty("entwurf").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Zustandskonflikte_liefern_409_mit_Code()
    {
        var leer = await AnlegenAsync();
        var freigegeben = await AnlegenAsync(("Eier", 300));
        await FreigebenAsync(freigegeben);
        var mitEntwurf = await AnlegenAsync(("Eier", 300));
        await FreigebenAsync(mitEntwurf);
        await _client.PostAsync($"/api/v1/rezepte/{mitEntwurf}/entwurf", content: null, _ct);

        var faelle = new (HttpResponseMessage Antwort, string Code)[]
        {
            (await FreigebenAsync(leer), "fassung.ohne_zutaten"),
            (await _client.DeleteAsync($"/api/v1/rezepte/{leer}/entwurf", _ct), "rezept.einzige_fassung"),
            (await FreigebenAsync(freigegeben), "rezept.kein_entwurf"),
            (await _client.DeleteAsync($"/api/v1/rezepte/{freigegeben}/entwurf", _ct), "rezept.kein_entwurf"),
            (await _client.PutAsJsonAsync($"/api/v1/rezepte/{freigegeben}/entwurf", new { zutaten = Array.Empty<object>() }, _ct), "rezept.kein_entwurf"),
            (await _client.PostAsync($"/api/v1/rezepte/{mitEntwurf}/entwurf", content: null, _ct), "rezept.entwurf_vorhanden"),
        };

        foreach (var (antwort, code) in faelle)
        {
            antwort.StatusCode.ShouldBe(HttpStatusCode.Conflict, code);
            (await CodeAsync(antwort)).ShouldBe(code);
        }
    }

    [Fact]
    public async Task Ungueltige_Zutaten_im_Entwurf_liefern_422_und_null_Eintraege_400()
    {
        var id = await AnlegenAsync(("Eier", 300));

        var ungueltig = await _client.PutAsJsonAsync($"/api/v1/rezepte/{id}/entwurf", new { zutaten = new[] { new { bezeichnung = "Eier", gramm = 0.001m } } }, _ct);
        var nullEintrag = await _client.PutAsJsonAsync($"/api/v1/rezepte/{id}/entwurf", new { zutaten = new object?[] { null } }, _ct);
        var ohneListe = await _client.PutAsJsonAsync($"/api/v1/rezepte/{id}/entwurf", new { }, _ct);

        ungueltig.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(ungueltig)).ShouldBe("gewicht.zu_genau");
        nullEintrag.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ohneListe.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unbekannte_Rezepte_und_Fassungen_liefern_404()
    {
        var id = await AnlegenAsync(("Eier", 300));
        var unbekannt = Guid.NewGuid();

        var antworten = new[]
        {
            await _client.GetAsync($"/api/v1/rezepte/{id}/fassungen/2", _ct),
            await _client.GetAsync($"/api/v1/rezepte/{id}/fassungen/0", _ct),
            await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=100&fassung=2", _ct),
            await _client.GetAsync($"/api/v1/rezepte/{unbekannt}/fassungen/1", _ct),
            await _client.PostAsync($"/api/v1/rezepte/{unbekannt}/entwurf", content: null, _ct),
            await _client.PutAsJsonAsync($"/api/v1/rezepte/{unbekannt}/entwurf", new { zutaten = Array.Empty<object>() }, _ct),
            await _client.DeleteAsync($"/api/v1/rezepte/{unbekannt}/entwurf", _ct),
            await FreigebenAsync(unbekannt),
        };

        antworten.Select(a => a.StatusCode).ShouldAllBe(status => status == HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task Fassungsnummer_kleiner_1_im_Query_liefert_400(string fassung)
    {
        var id = await AnlegenAsync(("Eier", 300));

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=100&fassung={fassung}", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Fassung_als_Text_im_Query_liefert_400()
    {
        var id = await AnlegenAsync(("Eier", 300));

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=100&fassung=zwei", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
