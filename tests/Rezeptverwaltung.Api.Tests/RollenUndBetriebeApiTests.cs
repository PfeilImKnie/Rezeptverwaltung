using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>Rechte je Rolle und strikte Trennung der Betriebe (ADR 0015), Ende-zu-Ende mit echten Tokens.</summary>
public sealed class RollenUndBetriebeApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static object Biskuit(string name = "Wiener Masse") => new
    {
        name,
        bezugsform = new { art = "ring", durchmesserCm = 26m, hoeheCm = 6m },
        zutaten = new[] { new { bezeichnung = "Eier", gramm = 300m }, new { bezeichnung = "Zucker", gramm = 150m } },
    };

    private async Task<Guid> AnlegenAlsAsync(Persona persona, bool freigeben = false)
    {
        using var client = await factory.CreateClientAlsAsync(persona);
        var antwort = await client.PostAsJsonAsync("/api/v1/rezepte", Biskuit(), _ct);
        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();
        if (freigeben)
        {
            (await client.PostAsync($"/api/v1/rezepte/{id}/entwurf/freigabe", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        return id;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage antwort) =>
        (await antwort.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static Task<HttpResponseMessage> BearbeitenAsync(HttpClient client, Guid id, CancellationToken ct) =>
        client.PutAsJsonAsync($"/api/v1/rezepte/{id}/entwurf", new { zutaten = new[] { new { bezeichnung = "Eier", gramm = 320m } } }, ct);

    [Fact]
    public async Task Mitarbeiter_liest_und_rechnet_um_aendert_aber_nichts()
    {
        var id = await AnlegenAlsAsync(Persona.InhaberA);
        using var client = await factory.CreateClientAlsAsync(Persona.MitarbeiterA);

        (await client.GetAsync($"/api/v1/rezepte/{id}", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/rezepte/{id}/fassungen/1", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=900", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/rezepte/{id}/skaliert/ring?durchmesserCm=18&hoeheCm=6", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var verboten = new[]
        {
            await client.PostAsJsonAsync("/api/v1/rezepte", Biskuit(), _ct),
            await BearbeitenAsync(client, id, _ct),
            await client.PostAsync($"/api/v1/rezepte/{id}/entwurf/freigabe", null, _ct),
            await client.DeleteAsync($"/api/v1/rezepte/{id}/entwurf", _ct),
        };
        foreach (var antwort in verboten)
        {
            antwort.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await CodeAsync(antwort)).ShouldBe("recht.fehlt");
        }
    }

    [Fact]
    public async Task Konditor_legt_an_und_bearbeitet_gibt_aber_nicht_frei()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.KonditorA);
        var id = await AnlegenAlsAsync(Persona.KonditorA);

        (await BearbeitenAsync(client, id, _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var freigabe = await client.PostAsync($"/api/v1/rezepte/{id}/entwurf/freigabe", null, _ct);

        freigabe.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(freigabe)).ShouldBe("recht.fehlt");
    }

    [Fact]
    public async Task Konditor_legt_neuen_Entwurf_an_und_verwirft_ihn()
    {
        var id = await AnlegenAlsAsync(Persona.InhaberA, freigeben: true);
        using var client = await factory.CreateClientAlsAsync(Persona.KonditorA);

        (await client.PostAsync($"/api/v1/rezepte/{id}/entwurf", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await client.DeleteAsync($"/api/v1/rezepte/{id}/entwurf", _ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Inhaber_gibt_den_Entwurf_eines_Konditors_frei()
    {
        var id = await AnlegenAlsAsync(Persona.KonditorA);
        using var client = await factory.CreateClientAlsAsync(Persona.InhaberA);

        (await client.PostAsync($"/api/v1/rezepte/{id}/entwurf/freigabe", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [MemberData(nameof(PersonenOhneRecht))]
    public async Task Ohne_Rolle_im_Betrieb_wird_auch_das_Lesen_verweigert(string persona, string code)
    {
        var id = await AnlegenAlsAsync(Persona.InhaberA);
        using var client = await factory.CreateClientAlsAsync(Persona.Alle.Single(p => p.Name == persona));

        var antwort = await client.GetAsync($"/api/v1/rezepte/{id}", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(antwort)).ShouldBe(code);
    }

    public static TheoryData<string, string> PersonenOhneRecht => new()
    {
        { Persona.OhneRolle.Name, "recht.fehlt" },
        { Persona.UnbekannteRolle.Name, "recht.fehlt" },
        { Persona.ZweiBetriebe.Name, "betrieb.nicht_eindeutig" },
    };

    [Fact]
    public async Task Rezepte_eines_anderen_Betriebs_existieren_fuer_ihn_nicht()
    {
        var idA = await AnlegenAlsAsync(Persona.InhaberA, freigeben: true);
        using var b = await factory.CreateClientAlsAsync(Persona.InhaberB);
        var fremd = new Dictionary<string, HttpResponseMessage>
        {
            ["lesen"] = await b.GetAsync($"/api/v1/rezepte/{idA}", _ct),
            ["fassung"] = await b.GetAsync($"/api/v1/rezepte/{idA}/fassungen/1", _ct),
            ["skalieren"] = await b.GetAsync($"/api/v1/rezepte/{idA}/skaliert?zielgewichtGramm=900", _ct),
            ["ring"] = await b.GetAsync($"/api/v1/rezepte/{idA}/skaliert/ring?durchmesserCm=18&hoeheCm=6", _ct),
            ["blech"] = await b.GetAsync($"/api/v1/rezepte/{idA}/skaliert/blech?laengeCm=60&breiteCm=40&hoeheCm=2", _ct),
            ["entwurf"] = await b.PostAsync($"/api/v1/rezepte/{idA}/entwurf", null, _ct),
            ["bearbeiten"] = await BearbeitenAsync(b, idA, _ct),
            ["freigeben"] = await b.PostAsync($"/api/v1/rezepte/{idA}/entwurf/freigabe", null, _ct),
            ["verwerfen"] = await b.DeleteAsync($"/api/v1/rezepte/{idA}/entwurf", _ct),
        };

        foreach (var (aktion, antwort) in fremd)
        {
            antwort.StatusCode.ShouldBe(HttpStatusCode.NotFound, aktion);
        }

        using var a = await factory.CreateClientAlsAsync(Persona.InhaberA);
        var rezept = await a.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{idA}", _ct);
        rezept.GetProperty("entwurf").ValueKind.ShouldBe(JsonValueKind.Null);
        rezept.GetProperty("fassungen").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Fremdes_Rezept_ist_von_einem_nie_vorhandenen_nicht_zu_unterscheiden()
    {
        var idA = await AnlegenAlsAsync(Persona.InhaberA);
        using var b = await factory.CreateClientAlsAsync(Persona.InhaberB);

        var fremd = await b.GetAsync($"/api/v1/rezepte/{idA}", _ct);
        var nieVorhanden = await b.GetAsync($"/api/v1/rezepte/{Guid.NewGuid()}", _ct);

        fremd.StatusCode.ShouldBe(nieVorhanden.StatusCode);
        (await OhneTraceIdAsync(fremd)).ShouldBe(await OhneTraceIdAsync(nieVorhanden));
    }

    private static async Task<string> OhneTraceIdAsync(HttpResponseMessage antwort)
    {
        var inhalt = (await antwort.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>())!;
        inhalt.Remove("traceId");
        return JsonSerializer.Serialize(inhalt.OrderBy(e => e.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Jeder_Betrieb_legt_eigene_Rezepte_an_und_sieht_nur_diese()
    {
        var idA = await AnlegenAlsAsync(Persona.InhaberA);
        var idB = await AnlegenAlsAsync(Persona.InhaberB);
        using var a = await factory.CreateClientAlsAsync(Persona.InhaberA);
        using var b = await factory.CreateClientAlsAsync(Persona.InhaberB);

        (await a.GetAsync($"/api/v1/rezepte/{idA}", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await a.GetAsync($"/api/v1/rezepte/{idB}", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.GetAsync($"/api/v1/rezepte/{idB}", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await b.GetAsync($"/api/v1/rezepte/{idA}", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Fehlendes_Recht_wird_vor_nicht_vorhandenem_Rezept_gemeldet()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.MitarbeiterA);

        (await client.PostAsync($"/api/v1/rezepte/{Guid.NewGuid()}/entwurf/freigabe", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Hundert_Zutaten_sind_erlaubt_hunderteins_nicht()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.InhaberA);
        static object Mit(int anzahl) => new { name = "Viel", zutaten = Enumerable.Range(1, anzahl).Select(i => new { bezeichnung = $"Z{i}", gramm = 1m }) };

        (await client.PostAsJsonAsync("/api/v1/rezepte", Mit(100), _ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var zuViel = await client.PostAsJsonAsync("/api/v1/rezepte", Mit(101), _ct);

        zuViel.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(zuViel)).ShouldBe("fassung.zu_viele_zutaten");
    }

    [Fact]
    public async Task Uebergrosse_Anfrage_wird_abgewiesen()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.InhaberA);
        var riesig = new string('x', 1_100_000);

        var antwort = await client.PostAsync(
            "/api/v1/rezepte",
            new StringContent($$"""{"name":"{{riesig}}","zutaten":[]}""", System.Text.Encoding.UTF8, "application/json"),
            _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        antwort.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }
}
