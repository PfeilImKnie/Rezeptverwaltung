using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>Rezeptliste mit Suche und Abfrage der eigenen Rechte über HTTP (Grundlage der Weboberfläche).</summary>
public sealed class RezeptlisteApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Eindeutiger Präfix je Test, weil sich die Tests einer Klasse die Datenbank teilen.</summary>
    private readonly string _praefix = $"T{Guid.NewGuid():N}"[..10];

    private async Task<Guid> AnlegenAsync(string name, Persona? persona = null)
    {
        using var client = await factory.CreateClientAlsAsync(persona ?? Persona.InhaberA);
        var antwort = await client.PostAsJsonAsync(
            "/api/v1/rezepte", new { name, zutaten = new[] { new { bezeichnung = "Eier", gramm = 300 } } }, _ct);
        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> ListeAsync(string suche, Persona? persona = null)
    {
        using var client = await factory.CreateClientAlsAsync(persona ?? Persona.MitarbeiterA);
        var antwort = await client.GetAsync($"/api/v1/rezepte?suche={Uri.EscapeDataString(suche)}", _ct);
        antwort.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct);
    }

    private static string[] Namen(JsonElement liste) => liste.EnumerateArray().Select(r => r.GetProperty("name").GetString()!).ToArray();

    [Fact]
    public async Task Liste_ist_sortiert_und_gefiltert()
    {
        await AnlegenAsync($"{_praefix} Sachertorte");
        await AnlegenAsync($"{_praefix} Käsekuchen");
        await AnlegenAsync($"{_praefix} biskuit");

        Namen(await ListeAsync(_praefix)).ShouldBe([$"{_praefix} biskuit", $"{_praefix} Käsekuchen", $"{_praefix} Sachertorte"]);
        Namen(await ListeAsync($"{_praefix} KASE")).ShouldBe([$"{_praefix} Käsekuchen"]);
    }

    [Fact]
    public async Task Eintrag_hat_die_Felder_aus_dem_Vertrag()
    {
        var id = await AnlegenAsync($"{_praefix} Biskuit");

        var eintrag = (await ListeAsync(_praefix)).EnumerateArray().ShouldHaveSingleItem();

        eintrag.EnumerateObject().Select(p => p.Name).Order()
            .ShouldBe(["aktuelleFassung", "entwurf", "id", "massgeblicheFassung", "name"]);
        eintrag.GetProperty("id").GetGuid().ShouldBe(id);
        eintrag.GetProperty("massgeblicheFassung").GetInt32().ShouldBe(1);
        eintrag.GetProperty("aktuelleFassung").ValueKind.ShouldBe(JsonValueKind.Null);
        eintrag.GetProperty("entwurf").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Ohne_Suche_kommen_alle_eigenen_Rezepte()
    {
        await AnlegenAsync($"{_praefix} Eigenes");
        await AnlegenAsync($"{_praefix} Fremdes", Persona.InhaberB);

        using var client = await factory.CreateClientAlsAsync(Persona.MitarbeiterA);
        var alle = await client.GetFromJsonAsync<JsonElement>("/api/v1/rezepte", _ct);

        Namen(alle).ShouldContain($"{_praefix} Eigenes");
        Namen(alle).ShouldNotContain($"{_praefix} Fremdes");
        Namen(await ListeAsync(_praefix, Persona.InhaberB)).ShouldBe([$"{_praefix} Fremdes"]);
    }

    [Fact]
    public async Task Leere_Liste_ist_ein_leeres_Array() =>
        (await ListeAsync($"{_praefix}-gibt-es-nicht")).GetArrayLength().ShouldBe(0);

    [Fact]
    public async Task Zu_lange_Suche_ergibt_422_mit_Code()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.MitarbeiterA);

        var antwort = await client.GetAsync($"/api/v1/rezepte?suche={new string('a', 101)}", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("code").GetString().ShouldBe("suche.zu_lang");
    }

    [Fact]
    public async Task Ohne_Rolle_gibt_es_keine_Liste()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.OhneRolle);

        var antwort = await client.GetAsync("/api/v1/rezepte", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("code").GetString().ShouldBe("recht.fehlt");
    }

    public static TheoryData<string, bool, bool, bool> Matrix => new()
    {
        { Persona.MitarbeiterA.Name, true, false, false },
        { Persona.KonditorA.Name, true, true, false },
        { Persona.InhaberA.Name, true, true, true },
        { Persona.OhneRolle.Name, false, false, false },
        { Persona.UnbekannteRolle.Name, false, false, false },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Berechtigungen_je_Rolle(string persona, bool lesen, bool bearbeiten, bool freigeben)
    {
        using var client = await factory.CreateClientAlsAsync(Persona.Alle.Single(p => p.Name == persona));

        var rechte = await client.GetFromJsonAsync<JsonElement>("/api/v1/rezepte/berechtigungen", _ct);

        rechte.EnumerateObject().Select(p => p.Name).Order().ShouldBe(["bearbeiten", "freigeben", "lesen"]);
        rechte.GetProperty("lesen").GetBoolean().ShouldBe(lesen);
        rechte.GetProperty("bearbeiten").GetBoolean().ShouldBe(bearbeiten);
        rechte.GetProperty("freigeben").GetBoolean().ShouldBe(freigeben);
    }

    [Fact]
    public async Task Berechtigungen_bei_mehreren_Betrieben_ergeben_403()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.ZweiBetriebe);

        var antwort = await client.GetAsync("/api/v1/rezepte/berechtigungen", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("code").GetString().ShouldBe("betrieb.nicht_eindeutig");
    }
}
