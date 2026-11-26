using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>
/// Unabhängiger Test von <c>GET /api/v1/rezepte?suche=</c> und <c>GET /api/v1/rezepte/berechtigungen</c>:
/// Grenzwerte der Suche (100 Codepoints), Akzente in der Adresse, Rollen und Betriebe.
/// </summary>
public sealed class RezeptlisteGrenzfallApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Eindeutiger Präfix je Test, weil sich die Tests einer Klasse die Datenbank teilen.</summary>
    private readonly string _praefix = $"U{Guid.NewGuid():N}"[..10];

    private async Task AnlegenAsync(string name, Persona? persona = null)
    {
        using var client = await factory.CreateClientAlsAsync(persona ?? Persona.KonditorA);
        using var antwort = await client.PostAsJsonAsync(
            "/api/v1/rezepte", new { name, zutaten = new[] { new { bezeichnung = "Mehl", gramm = 250 } } }, _ct);
        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private async Task<HttpResponseMessage> SuchenAsync(string suche, Persona? persona = null)
    {
        using var client = await factory.CreateClientAlsAsync(persona ?? Persona.MitarbeiterA);
        return await client.GetAsync($"/api/v1/rezepte?suche={Uri.EscapeDataString(suche)}", _ct);
    }

    private async Task<string[]> NamenAsync(string suche, Persona? persona = null)
    {
        using var antwort = await SuchenAsync(suche, persona);
        antwort.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).EnumerateArray()
            .Select(r => r.GetProperty("name").GetString()!).ToArray();
    }

    [Fact]
    public async Task Suche_mit_genau_100_Zeichen_ist_erlaubt()
    {
        using var antwort = await SuchenAsync(new string('a', 100));

        antwort.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Suche_mit_100_Emoji_zaehlt_Codepoints_nicht_UTF16_Einheiten()
    {
        using var antwort = await SuchenAsync(string.Concat(Enumerable.Repeat("🎂", 100)));

        antwort.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Suche_mit_101_Emoji_ergibt_422_als_Problem_Details()
    {
        using var antwort = await SuchenAsync(string.Concat(Enumerable.Repeat("🎂", 101)));

        antwort.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        antwort.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("code").GetString().ShouldBe("suche.zu_lang");
    }

    [Theory]
    [InlineData("käse")]
    [InlineData("KÄSE")]
    [InlineData("kase")]
    public async Task Umlaut_in_der_Adresse_findet_auch_den_Namen_ohne_Umlaut(string suchwort)
    {
        await AnlegenAsync($"{_praefix} Kasekuchen");
        await AnlegenAsync($"{_praefix} Käsesahne");

        (await NamenAsync($"{_praefix} {suchwort}")).ShouldBe([$"{_praefix} Kasekuchen", $"{_praefix} Käsesahne"]);
    }

    [Fact]
    public async Task Suche_nur_aus_Leerraum_liefert_alle_eigenen_Rezepte()
    {
        await AnlegenAsync($"{_praefix} Mohnstrudel");

        (await NamenAsync(" \t ")).ShouldContain($"{_praefix} Mohnstrudel");
    }

    [Fact]
    public async Task Liste_sortiert_Umlaute_wie_den_Grundbuchstaben()
    {
        await AnlegenAsync($"{_praefix} Zimtsterne");
        await AnlegenAsync($"{_praefix} Orangencreme");
        await AnlegenAsync($"{_praefix} Ölbiskuit");

        (await NamenAsync(_praefix)).ShouldBe([$"{_praefix} Ölbiskuit", $"{_praefix} Orangencreme", $"{_praefix} Zimtsterne"]);
    }

    [Fact]
    public async Task Fremdes_Rezept_wird_auch_mit_seinem_genauen_Namen_nicht_gefunden()
    {
        await AnlegenAsync($"{_praefix} Geheimrezept", Persona.InhaberB);

        (await NamenAsync($"{_praefix} Geheimrezept", Persona.InhaberA)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("konditor-a")]
    [InlineData("inhaber-a")]
    public async Task Alle_Rollen_mit_Leserecht_bekommen_die_Liste(string persona)
    {
        await AnlegenAsync($"{_praefix} Biskuit");

        (await NamenAsync(_praefix, Persona.Alle.Single(p => p.Name == persona))).ShouldBe([$"{_praefix} Biskuit"]);
    }

    [Theory]
    [InlineData("zwei-betriebe", "betrieb.nicht_eindeutig")]
    [InlineData("unbekannte-rolle", "recht.fehlt")]
    public async Task Ohne_eindeutiges_Leserecht_gibt_es_keine_Liste(string persona, string code)
    {
        using var antwort = await SuchenAsync("", Persona.Alle.Single(p => p.Name == persona));

        antwort.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task Recht_wird_vor_der_Laenge_der_Suche_geprueft()
    {
        using var antwort = await SuchenAsync(new string('a', 101), Persona.OhneRolle);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unbekannte_Rolle_in_einem_fremden_Betrieb_nimmt_kein_Recht_weg()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.MitarbeiterAMitFremderUnbekannterRolle);

        var rechte = await client.GetFromJsonAsync<JsonElement>("/api/v1/rezepte/berechtigungen", _ct);

        rechte.GetProperty("lesen").GetBoolean().ShouldBeTrue();
        rechte.GetProperty("bearbeiten").GetBoolean().ShouldBeFalse();
        rechte.GetProperty("freigeben").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Berechtigungen_passen_zur_tatsaechlichen_Pruefung_der_Operation()
    {
        // Was die Oberfläche ausblendet, muss die API auch verbieten (und umgekehrt).
        foreach (var persona in new[] { Persona.MitarbeiterA, Persona.KonditorA, Persona.InhaberA, Persona.OhneRolle })
        {
            using var client = await factory.CreateClientAlsAsync(persona);
            var rechte = await client.GetFromJsonAsync<JsonElement>("/api/v1/rezepte/berechtigungen", _ct);

            using var liste = await client.GetAsync("/api/v1/rezepte", _ct);
            using var anlegen = await client.PostAsJsonAsync(
                "/api/v1/rezepte", new { name = $"{_praefix} {persona.Name}", zutaten = new[] { new { bezeichnung = "Mehl", gramm = 1 } } }, _ct);

            (liste.StatusCode == HttpStatusCode.OK).ShouldBe(rechte.GetProperty("lesen").GetBoolean(), persona.Name);
            (anlegen.StatusCode == HttpStatusCode.Created).ShouldBe(rechte.GetProperty("bearbeiten").GetBoolean(), persona.Name);
        }
    }
}
