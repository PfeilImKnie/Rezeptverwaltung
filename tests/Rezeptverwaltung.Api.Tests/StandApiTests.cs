using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>
/// Schutz vor still verlorenen Änderungen über Anfragegrenzen hinweg (ADR 0013): ETag in Antworten, If-Match bei
/// Änderungen am Entwurf. Szenario aus dem unabhängigen Test: Tablet lädt, Alexa ändert, Tablet speichert alten Stand.
/// </summary>
public sealed class StandApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<(Guid Id, EntityTagHeaderValue Etag)> RezeptMitEntwurfAsync()
    {
        var angelegt = await _client.PostAsJsonAsync(
            "/api/v1/rezepte",
            new { name = "Ganache", zutaten = new[] { new { bezeichnung = "Kuvertüre", gramm = 200m }, new { bezeichnung = "Sahne", gramm = 200m } } },
            _ct);
        angelegt.Headers.ETag.ShouldNotBeNull();
        var id = (await angelegt.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();
        return (id, angelegt.Headers.ETag!);
    }

    private Task<HttpResponseMessage> BearbeitenAsync(Guid id, EntityTagHeaderValue? stand, decimal kuvertuere)
    {
        var anfrage = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/rezepte/{id}/entwurf")
        {
            Content = JsonContent.Create(new { zutaten = new[] { new { bezeichnung = "Kuvertüre", gramm = kuvertuere } } }),
        };
        if (stand is not null)
        {
            anfrage.Headers.IfMatch.Add(stand);
        }

        return _client.SendAsync(anfrage, _ct);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage antwort) =>
        (await antwort.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Tablet_mit_veraltetem_Stand_ueberschreibt_Alexas_Aenderung_nicht()
    {
        var (id, gelesenVomTablet) = await RezeptMitEntwurfAsync();
        var gelesenVonAlexa = gelesenVomTablet;

        var alexa = await BearbeitenAsync(id, gelesenVonAlexa, kuvertuere: 210);
        var tablet = await BearbeitenAsync(id, gelesenVomTablet, kuvertuere: 190);

        alexa.StatusCode.ShouldBe(HttpStatusCode.OK);
        tablet.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        tablet.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await CodeAsync(tablet)).ShouldBe("rezept.veraltet");
        var rezept = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}", _ct);
        rezept.GetProperty("zutaten")[0].GetProperty("gramm").GetDecimal().ShouldBe(210m);
    }

    [Fact]
    public async Task Mit_dem_ETag_der_letzten_Antwort_geht_es_weiter()
    {
        var (id, stand) = await RezeptMitEntwurfAsync();

        var erste = await BearbeitenAsync(id, stand, kuvertuere: 210);
        var zweite = await BearbeitenAsync(id, erste.Headers.ETag, kuvertuere: 220);

        erste.Headers.ETag.ShouldNotBe(stand);
        zweite.StatusCode.ShouldBe(HttpStatusCode.OK);
        zweite.Headers.ETag.ShouldNotBe(erste.Headers.ETag);
    }

    [Fact]
    public async Task Abfrage_liefert_denselben_ETag_wie_die_letzte_Aenderung()
    {
        var (id, stand) = await RezeptMitEntwurfAsync();
        var geaendert = await BearbeitenAsync(id, stand, kuvertuere: 210);

        var rezept = await _client.GetAsync($"/api/v1/rezepte/{id}", _ct);
        var fassung = await _client.GetAsync($"/api/v1/rezepte/{id}/fassungen/1", _ct);

        rezept.Headers.ETag.ShouldBe(geaendert.Headers.ETag);
        fassung.Headers.ETag.ShouldBe(geaendert.Headers.ETag);
    }

    [Fact]
    public async Task Alle_Aenderungen_am_Entwurf_ohne_If_Match_liefern_428_und_aendern_nichts()
    {
        var (id, _) = await RezeptMitEntwurfAsync();

        var antworten = new[]
        {
            await BearbeitenAsync(id, stand: null, kuvertuere: 1),
            await _client.PostAsync($"/api/v1/rezepte/{id}/entwurf/freigabe", content: null, _ct),
            await _client.PostAsync($"/api/v1/rezepte/{id}/entwurf", content: null, _ct),
            await _client.DeleteAsync($"/api/v1/rezepte/{id}/entwurf", _ct),
        };

        foreach (var antwort in antworten)
        {
            antwort.StatusCode.ShouldBe((HttpStatusCode)428);
            (await CodeAsync(antwort)).ShouldBe("anfrage.if_match_fehlt");
        }

        var rezept = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}", _ct);
        rezept.GetProperty("entwurf").GetInt32().ShouldBe(1);
        rezept.GetProperty("gesamtgewichtGramm").GetDecimal().ShouldBe(400m);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"1\"")]
    [InlineData("\"0\"")]
    [InlineData("irgendwas")]
    [InlineData("\"\"")]
    [InlineData("\"1\", \"2\"")]
    public async Task Fremder_schwacher_oder_Platzhalter_Stand_liefert_412(string ifMatch)
    {
        var (id, _) = await RezeptMitEntwurfAsync();
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rezepte/{id}/entwurf/freigabe");
        anfrage.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        var antwort = await _client.SendAsync(anfrage, _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Leerer_Stand_gilt_als_fehlend_und_liefert_428(string ifMatch)
    {
        var (id, _) = await RezeptMitEntwurfAsync();
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rezepte/{id}/entwurf/freigabe");
        anfrage.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        var antwort = await _client.SendAsync(anfrage, _ct);

        antwort.StatusCode.ShouldBe((HttpStatusCode)428);
    }

    [Fact]
    public async Task Aktueller_Stand_ohne_Anfuehrungszeichen_ist_kein_gueltiges_ETag_und_liefert_412()
    {
        var (id, stand) = await RezeptMitEntwurfAsync();
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/rezepte/{id}/entwurf/freigabe");
        anfrage.Headers.TryAddWithoutValidation("If-Match", stand.Tag.Trim('"'));

        var antwort = await _client.SendAsync(anfrage, _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Unbekanntes_Rezept_liefert_404_auch_ohne_If_Match()
    {
        var antwort = await _client.PostAsync($"/api/v1/rezepte/{Guid.NewGuid()}/entwurf/freigabe", content: null, _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Veralteter_Stand_hat_Vorrang_vor_ungueltiger_Eingabe()
    {
        var (id, stand) = await RezeptMitEntwurfAsync();
        await BearbeitenAsync(id, stand, kuvertuere: 210);

        var antwort = await BearbeitenAsync(id, stand, kuvertuere: 0.001m);

        antwort.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }
}
