using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>Unabhängiger Test (pr-test): Fassungen über die API, Grenzwerte, Nebenläufigkeit und Abwärtskompatibilität.</summary>
public sealed class FassungenGrenzfallApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClientMitAktuellemStand();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<Guid> AnlegenAsync(params (string Bezeichnung, decimal Gramm)[] zutaten)
    {
        var antwort = await _client.PostAsJsonAsync(
            "/api/v1/rezepte",
            new { name = "Sachermasse", zutaten = zutaten.Select(z => new { bezeichnung = z.Bezeichnung, gramm = z.Gramm }) },
            _ct);
        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> FreigegebenAsync(params (string Bezeichnung, decimal Gramm)[] zutaten)
    {
        var id = await AnlegenAsync(zutaten);
        (await FreigebenAsync(id)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return id;
    }

    private Task<HttpResponseMessage> FreigebenAsync(Guid id) => _client.PostAsync($"/api/v1/rezepte/{id}/entwurf/freigabe", content: null, _ct);

    private Task<HttpResponseMessage> EntwurfAnlegenAsync(Guid id) => _client.PostAsync($"/api/v1/rezepte/{id}/entwurf", content: null, _ct);

    private Task<HttpResponseMessage> RohPutAsync(Guid id, string json) =>
        _client.PutAsync($"/api/v1/rezepte/{id}/entwurf", new StringContent(json, Encoding.UTF8, "application/json"), _ct);

    private async Task<JsonElement> JsonAsync(string pfad) => await _client.GetFromJsonAsync<JsonElement>(pfad, _ct);

    private static async Task<string?> CodeAsync(HttpResponseMessage antwort) =>
        (await antwort.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Fassungsnummer_eines_verworfenen_Entwurfs_wird_ueber_die_API_nie_wiederverwendet()
    {
        var id = await FreigegebenAsync(("Butter", 140));
        (await EntwurfAnlegenAsync(id)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await _client.DeleteAsync($"/api/v1/rezepte/{id}/entwurf", _ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var neu = await EntwurfAnlegenAsync(id);

        neu.Headers.Location!.ToString().ShouldBe($"/api/v1/rezepte/{id}/fassungen/3");
        (await neu.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("nummer").GetInt32().ShouldBe(3);
        (await _client.GetAsync($"/api/v1/rezepte/{id}/fassungen/2", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=100&fassung=2", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var rezept = await JsonAsync($"/api/v1/rezepte/{id}");
        rezept.GetProperty("fassungen").EnumerateArray().Select(f => f.GetProperty("nummer").GetInt32()).ShouldBe([1, 3]);
    }

    [Fact]
    public async Task Location_des_neuen_Entwurfs_liefert_die_Kopie_der_aktuellen_Fassung()
    {
        var id = await FreigegebenAsync(("Butter", 140), ("Zucker", 70), ("Zucker", 70));

        var angelegt = await EntwurfAnlegenAsync(id);
        var entwurf = await JsonAsync(angelegt.Headers.Location!.ToString());
        var fassung1 = await JsonAsync($"/api/v1/rezepte/{id}/fassungen/1");

        entwurf.GetProperty("status").GetString().ShouldBe("entwurf");
        entwurf.GetProperty("rezeptId").GetGuid().ShouldBe(id);
        entwurf.GetProperty("freigegebenAm").ValueKind.ShouldBe(JsonValueKind.Null);
        entwurf.GetProperty("zutaten").GetRawText().ShouldBe(fassung1.GetProperty("zutaten").GetRawText());
        entwurf.GetProperty("gesamtgewichtGramm").GetDecimal().ShouldBe(280m);
    }

    [Fact]
    public async Task Freigegebene_Fassung_bleibt_bytegleich_waehrend_der_Entwurf_bearbeitet_und_freigegeben_wird()
    {
        var id = await FreigegebenAsync(("Butter", 140), ("Eier", 280));
        var vorher = (await JsonAsync($"/api/v1/rezepte/{id}/fassungen/1")).GetRawText();

        await EntwurfAnlegenAsync(id);
        (await RohPutAsync(id, """{"zutaten":[{"bezeichnung":"Butter","gramm":1}]}""")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await FreigebenAsync(id)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await JsonAsync($"/api/v1/rezepte/{id}/fassungen/1")).GetRawText().ShouldBe(vorher);
    }

    [Fact]
    public async Task Zeitpunkte_werden_festgehalten_und_sind_plausibel()
    {
        var vorAnlegen = DateTimeOffset.UtcNow.AddSeconds(-5);
        var id = await AnlegenAsync(("Eier", 300));
        var entwurf1 = await JsonAsync($"/api/v1/rezepte/{id}/fassungen/1");
        var freigabe = await (await FreigebenAsync(id)).Content.ReadFromJsonAsync<JsonElement>(_ct);
        var nachFreigabe = DateTimeOffset.UtcNow.AddSeconds(5);

        var erstellt = entwurf1.GetProperty("erstelltAm").GetDateTimeOffset();
        erstellt.ShouldBeInRange(vorAnlegen, nachFreigabe);
        freigabe.GetProperty("erstelltAm").GetDateTimeOffset().ShouldBe(erstellt);
        freigabe.GetProperty("freigegebenAm").GetDateTimeOffset().ShouldBeInRange(erstellt, nachFreigabe);

        var uebersicht = (await JsonAsync($"/api/v1/rezepte/{id}")).GetProperty("fassungen")[0];
        uebersicht.GetProperty("erstelltAm").GetDateTimeOffset().ShouldBe(erstellt);
        uebersicht.GetProperty("freigegebenAm").GetDateTimeOffset().ShouldBe(freigabe.GetProperty("freigegebenAm").GetDateTimeOffset());
    }

    [Fact]
    public async Task Entwurf_bearbeiten_an_den_Grenzen_erhaelt_Werte_Duplikate_und_Reihenfolge()
    {
        var id = await AnlegenAsync(("Eier", 300));

        var antwort = await RohPutAsync(
            id,
            """{"zutaten":[{"bezeichnung":"Zucker","gramm":0.01},{"bezeichnung":"Mehl","gramm":1000000000},{"bezeichnung":"Zucker","gramm":0.01}]}""");

        antwort.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entwurf = await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct);
        entwurf.GetProperty("zutaten").EnumerateArray()
            .Select(z => (z.GetProperty("bezeichnung").GetString(), z.GetProperty("gramm").GetDecimal()))
            .ShouldBe([("Zucker", 0.01m), ("Mehl", 1_000_000_000m), ("Zucker", 0.01m)]);
        entwurf.GetProperty("gesamtgewichtGramm").GetDecimal().ShouldBe(1_000_000_000.02m);
        (await JsonAsync($"/api/v1/rezepte/{id}")).GetProperty("gesamtgewichtGramm").GetDecimal().ShouldBe(1_000_000_000.02m);
    }

    [Theory]
    [InlineData("0", "zutat.menge_null")]
    [InlineData("0.005", "gewicht.zu_genau")]
    [InlineData("-0.01", "gewicht.negativ")]
    [InlineData("1000000000.01", "gewicht.zu_gross")]
    public async Task Ungueltige_Menge_im_Entwurf_liefert_422_und_aendert_nichts(string gramm, string code)
    {
        var id = await AnlegenAsync(("Eier", 300));

        var antwort = await RohPutAsync(id, $$"""{"zutaten":[{"bezeichnung":"Mehl","gramm":{{gramm}}}]}""");

        antwort.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(antwort)).ShouldBe(code);
        (await JsonAsync($"/api/v1/rezepte/{id}/fassungen/1")).GetProperty("gesamtgewichtGramm").GetDecimal().ShouldBe(300m);
    }

    [Fact]
    public async Task Ungueltige_Eingabe_hat_Vorrang_vor_fehlendem_Entwurf()
    {
        var id = await FreigegebenAsync(("Eier", 300));

        var antwort = await RohPutAsync(id, """{"zutaten":[{"bezeichnung":"   ","gramm":1}]}""");

        antwort.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData("""{"zutaten":[{"bezeichnung":"Mehl","gramm":"100"}]}""")]
    [InlineData("""{"zutaten":null}""")]
    [InlineData("""{"zutaten":[{"bezeichnung":"Mehl"}]}""")]
    [InlineData("""[]""")]
    public async Task Formatfehler_beim_Bearbeiten_liefern_400(string json)
    {
        var id = await AnlegenAsync(("Eier", 300));

        (await RohPutAsync(id, json)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Zweiter_Entwurf_auf_leere_Liste_gesetzt_ist_nicht_freigebbar_und_nicht_skalierbar()
    {
        var id = await FreigegebenAsync(("Eier", 300));
        await EntwurfAnlegenAsync(id);
        (await RohPutAsync(id, """{"zutaten":[]}""")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var freigabe = await FreigebenAsync(id);
        var skaliertEntwurf = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=100&fassung=2", _ct);
        var skaliertMassgeblich = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=600", _ct);

        freigabe.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(freigabe)).ShouldBe("fassung.ohne_zutaten");
        skaliertEntwurf.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(skaliertEntwurf)).ShouldBe("rezept.ohne_zutaten");
        skaliertMassgeblich.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await skaliertMassgeblich.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("fassung").GetInt32().ShouldBe(1);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2147483647")]
    public async Task Nicht_vorhandene_Fassungsnummer_beim_Skalieren_liefert_4xx_ohne_500(string fassung)
    {
        var id = await AnlegenAsync(("Eier", 300));

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=100&fassung={fassung}", _ct);

        antwort.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("2147483648")]
    [InlineData("1.5")]
    [InlineData("")]
    public async Task Fassungsnummer_im_falschen_Format_beim_Skalieren_liefert_400(string fassung)
    {
        var id = await AnlegenAsync(("Eier", 300));

        var antwort = await _client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=100&fassung={fassung}", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("eins")]
    public async Task Ungueltige_Fassungsnummer_im_Pfad_liefert_404_ohne_500(string nummer)
    {
        var id = await AnlegenAsync(("Eier", 300));

        (await _client.GetAsync($"/api/v1/rezepte/{id}/fassungen/{nummer}", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tablet_und_Alexa_legen_gleichzeitig_Entwuerfe_an_genau_einer_entsteht_der_Rest_ist_409_oder_412()
    {
        var id = await FreigegebenAsync(("Butter", 140), ("Eier", 280));

        var antworten = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => EntwurfAnlegenAsync(id)));

        antworten.Count(a => a.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        foreach (var abgewiesen in antworten.Where(a => a.StatusCode != HttpStatusCode.Created))
        {
            // Seit ETag/If-Match (ADR 0013) bekommt ein Client mit inzwischen veraltetem Stand 412 statt 409.
            abgewiesen.StatusCode.ShouldBeOneOf(HttpStatusCode.Conflict, HttpStatusCode.PreconditionFailed);
            var code = await CodeAsync(abgewiesen);
            if (abgewiesen.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                code.ShouldBe("rezept.veraltet");
            }
            else
            {
                code.ShouldBeOneOf("rezept.entwurf_vorhanden", "rezept.gleichzeitig_geaendert");
            }
        }

        var rezept = await JsonAsync($"/api/v1/rezepte/{id}");
        rezept.GetProperty("fassungen").EnumerateArray().Count(f => f.GetProperty("status").GetString() == "entwurf").ShouldBe(1);
        rezept.GetProperty("entwurf").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Gleichzeitige_Bearbeitungen_liefern_nur_200_409_oder_412_und_der_Entwurf_entspricht_einer_erfolgreichen()
    {
        var id = await AnlegenAsync(("Eier", 300));

        var antworten = await Task.WhenAll(Enumerable.Range(1, 8).Select(i =>
            RohPutAsync(id, $$"""{"zutaten":[{"bezeichnung":"Eier","gramm":{{300 + i}}}]}""")));

        // Seit ETag/If-Match (ADR 0013) ist auch 412 (veralteter Stand) eine zulässige Abweisung.
        antworten.ShouldAllBe(a => a.StatusCode == HttpStatusCode.OK || a.StatusCode == HttpStatusCode.Conflict || a.StatusCode == HttpStatusCode.PreconditionFailed);
        var erfolgreich = new List<decimal>();
        foreach (var antwort in antworten)
        {
            var body = await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct);
            if (antwort.StatusCode == HttpStatusCode.OK)
            {
                erfolgreich.Add(body.GetProperty("gesamtgewichtGramm").GetDecimal());
            }
            else
            {
                // 409: Kollision beim Speichern; 412: Stand war beim Senden schon veraltet (ADR 0013).
                body.GetProperty("code").GetString().ShouldBe(
                    antwort.StatusCode == HttpStatusCode.Conflict ? "rezept.gleichzeitig_geaendert" : "rezept.veraltet");
            }
        }

        erfolgreich.ShouldNotBeEmpty();
        erfolgreich.ShouldContain((await JsonAsync($"/api/v1/rezepte/{id}/fassungen/1")).GetProperty("gesamtgewichtGramm").GetDecimal());
    }

    [Fact]
    public async Task Bisherige_Felder_bleiben_in_Anlegen_Abrufen_und_Skalieren_erhalten()
    {
        var angelegt = await _client.PostAsJsonAsync(
            "/api/v1/rezepte",
            new { name = "Mürbeteig", zutaten = new[] { new { bezeichnung = "Mehl", gramm = 300m }, new { bezeichnung = "Butter", gramm = 200m } } },
            _ct);
        var body = await angelegt.Content.ReadFromJsonAsync<JsonElement>(_ct);
        var id = body.GetProperty("id").GetGuid();

        angelegt.Headers.Location!.ToString().ShouldBe($"/api/v1/rezepte/{id}");
        foreach (var rezept in new[] { body, await JsonAsync($"/api/v1/rezepte/{id}") })
        {
            rezept.GetProperty("name").GetString().ShouldBe("Mürbeteig");
            rezept.GetProperty("zutaten").GetArrayLength().ShouldBe(2);
            rezept.GetProperty("gesamtgewichtGramm").GetDecimal().ShouldBe(500m);
        }

        // Ein neu angelegtes Rezept (nur Entwurf) bleibt wie bisher sofort skalierbar.
        var skaliert = await JsonAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=1000");
        skaliert.GetProperty("rezeptId").GetGuid().ShouldBe(id);
        skaliert.GetProperty("name").GetString().ShouldBe("Mürbeteig");
        skaliert.GetProperty("zielgewichtGramm").GetDecimal().ShouldBe(1000m);
        skaliert.GetProperty("zutaten").EnumerateArray().Select(z => z.GetProperty("gramm").GetDecimal()).ShouldBe([600m, 400m]);
    }
}
