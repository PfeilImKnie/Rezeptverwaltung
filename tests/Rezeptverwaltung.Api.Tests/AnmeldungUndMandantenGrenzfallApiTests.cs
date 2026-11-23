using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>
/// Unabhängiger Test zu Issue #6 / ADR 0015: Grenzfälle der Anmeldung, der Rollen, der Trennung der Betriebe und der
/// Eingabegrenzen, abgeleitet aus den Akzeptanzkriterien.
/// </summary>
public sealed class AnmeldungUndMandantenGrenzfallApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static object Sachertorte(int zutaten = 2) => new
    {
        name = "Sachertorte",
        bezugsform = new { art = "ring", durchmesserCm = 26m, hoeheCm = 6m },
        zutaten = Enumerable.Range(1, zutaten).Select(i => new { bezeichnung = $"Zutat {i}", gramm = 10m * i }),
    };

    private async Task<Guid> AnlegenAlsAsync(Persona persona, bool freigeben = false)
    {
        using var client = await factory.CreateClientAlsAsync(persona);
        var antwort = await client.PostAsJsonAsync("/api/v1/rezepte", Sachertorte(), _ct);
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

    private static JsonElement Nutzdaten(string token)
    {
        var teil = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        teil = teil.PadRight(teil.Length + ((4 - (teil.Length % 4)) % 4), '=');
        return JsonDocument.Parse(Convert.FromBase64String(teil)).RootElement.Clone();
    }

    private static JsonElement Kopf(string token)
    {
        var teil = token.Split('.')[0].Replace('-', '+').Replace('_', '/');
        teil = teil.PadRight(teil.Length + ((4 - (teil.Length % 4)) % 4), '=');
        return JsonDocument.Parse(Convert.FromBase64String(teil)).RootElement.Clone();
    }

    // ---------- (1) Anmeldung ----------

    [Fact]
    public async Task Abgelaufener_Token_wird_abgewiesen()
    {
        var token = await TestLogin.TokenAsync(Persona.Abgelaufen);
        // Vorbedingung: Der Login-Server hat wirklich einen abgelaufenen Token ausgestellt.
        Nutzdaten(token).GetProperty("exp").GetInt64().ShouldBeLessThan(DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeSeconds());
        using var client = factory.CreateClientOhneAnmeldung();
        client.DefaultRequestHeaders.Authorization = TestLogin.Header(token);

        var antwort = await client.GetAsync($"/api/v1/rezepte/{Guid.NewGuid()}", _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Abgelaufener_Token_darf_kein_Rezept_anlegen()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.Abgelaufen);

        (await client.PostAsJsonAsync("/api/v1/rezepte", Sachertorte(), _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Noch_nicht_gueltiger_Token_wird_abgewiesen()
    {
        var token = await TestLogin.TokenAsync(Persona.NochNichtGueltig);
        Nutzdaten(token).GetProperty("nbf").GetInt64().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds());
        using var client = factory.CreateClientOhneAnmeldung();
        client.DefaultRequestHeaders.Authorization = TestLogin.Header(token);

        (await client.GetAsync($"/api/v1/rezepte/{Guid.NewGuid()}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mit_eigenem_Schluessel_signierter_Token_mit_passendem_Aussteller_und_Zielgruppe_wird_abgewiesen()
    {
        // Angreifer kennt Aussteller, Zielgruppe und Key-ID, hat aber nicht den privaten Schlüssel des IdP.
        var echt = await TestLogin.TokenAsync(Persona.InhaberA);
        var nutzdaten = Nutzdaten(echt);
        using var rsa = RSA.Create(2048);
        var schluessel = new RsaSecurityKey(rsa) { KeyId = Kopf(echt).GetProperty("kid").GetString() };
        var gefaelscht = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = nutzdaten.GetProperty("iss").GetString(),
            Audience = TestLogin.Zielgruppe,
            Expires = DateTime.UtcNow.AddHours(1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "angreifer",
                ["urn:zitadel:iam:org:project:roles"] = JsonDocument.Parse("""{"inhaber":{"betrieb-a":"a.test"}}""").RootElement,
            },
            SigningCredentials = new SigningCredentials(schluessel, SecurityAlgorithms.RsaSha256),
        });
        using var client = factory.CreateClientOhneAnmeldung();
        client.DefaultRequestHeaders.Authorization = TestLogin.Header(gefaelscht);

        (await client.GetAsync($"/api/v1/rezepte/{Guid.NewGuid()}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/v1/rezepte", Sachertorte(), _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Veraenderte_Rollen_im_echten_Token_machen_ihn_ungueltig()
    {
        // Mitarbeiter/in ändert im eigenen Token die Rolle auf Inhaber/in, die Signatur bleibt die alte.
        var teile = (await TestLogin.TokenAsync(Persona.MitarbeiterA)).Split('.');
        var json = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(teile[1])).Replace("\"mitarbeiter\"", "\"inhaber\"", StringComparison.Ordinal);
        using var client = factory.CreateClientOhneAnmeldung();
        client.DefaultRequestHeaders.Authorization = TestLogin.Header($"{teile[0]}.{Base64UrlEncoder.Encode(json)}.{teile[2]}");

        (await client.PostAsJsonAsync("/api/v1/rezepte", Sachertorte(), _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Basic")]
    [InlineData("Token")]
    public async Task Token_mit_anderem_Schema_als_Bearer_wird_abgewiesen(string schema)
    {
        using var client = factory.CreateClientOhneAnmeldung();
        client.DefaultRequestHeaders.Authorization = new(schema, await TestLogin.TokenAsync(Persona.InhaberA));

        (await client.GetAsync($"/api/v1/rezepte/{Guid.NewGuid()}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_in_der_Adresse_statt_im_Header_wird_nicht_angenommen()
    {
        using var client = factory.CreateClientOhneAnmeldung();
        var token = await TestLogin.TokenAsync(Persona.InhaberA);

        (await client.GetAsync($"/api/v1/rezepte/{Guid.NewGuid()}?access_token={token}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Leerer_Bearer_Token_wird_abgewiesen()
    {
        using var client = factory.CreateClientOhneAnmeldung();
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/rezepte/{Guid.NewGuid()}");
        anfrage.Headers.TryAddWithoutValidation("Authorization", "Bearer ");

        (await client.SendAsync(anfrage, _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- (3) Rollen ----------

    [Fact]
    public async Task Rollen_in_zwei_Betrieben_duerfen_auch_nicht_anlegen()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.ZweiBetriebe);

        var antwort = await client.PostAsJsonAsync("/api/v1/rezepte", Sachertorte(), _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(antwort)).ShouldBe("betrieb.nicht_eindeutig");
    }

    [Fact]
    public async Task Unbekannte_Rolle_darf_nicht_anlegen()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.UnbekannteRolle);

        var antwort = await client.PostAsJsonAsync("/api/v1/rezepte", Sachertorte(), _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(antwort)).ShouldBe("recht.fehlt");
    }

    [Fact]
    public async Task Rollenname_mit_anderer_Schreibweise_gibt_kein_Recht()
    {
        var id = await AnlegenAlsAsync(Persona.InhaberA);
        using var client = await factory.CreateClientAlsAsync(Persona.RolleGrossgeschrieben);

        var lesen = await client.GetAsync($"/api/v1/rezepte/{id}", _ct);
        var freigeben = await client.PostAsync($"/api/v1/rezepte/{id}/entwurf/freigabe", null, _ct);

        lesen.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(lesen)).ShouldBe("recht.fehlt");
        freigeben.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unbekannte_Rolle_in_einem_anderen_Betrieb_macht_den_Betrieb_nicht_mehrdeutig()
    {
        var id = await AnlegenAlsAsync(Persona.InhaberA);
        using var client = await factory.CreateClientAlsAsync(Persona.MitarbeiterAMitFremderUnbekannterRolle);

        (await client.GetAsync($"/api/v1/rezepte/{id}", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/rezepte/{id}/skaliert/ring?durchmesserCm=18&hoeheCm=6", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var anlegen = await client.PostAsJsonAsync("/api/v1/rezepte", Sachertorte(), _ct);
        anlegen.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(anlegen)).ShouldBe("recht.fehlt");
    }

    [Fact]
    public async Task Mitarbeiter_rechnet_auch_auf_ein_Blech_um()
    {
        var id = await AnlegenAlsAsync(Persona.InhaberA, freigeben: true);
        using var client = await factory.CreateClientAlsAsync(Persona.MitarbeiterA);

        (await client.GetAsync($"/api/v1/rezepte/{id}/skaliert/blech?laengeCm=60&breiteCm=40&hoeheCm=2", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Mitarbeiter_darf_keinen_neuen_Entwurf_anlegen()
    {
        var id = await AnlegenAlsAsync(Persona.InhaberA, freigeben: true);
        using var client = await factory.CreateClientAlsAsync(Persona.MitarbeiterA);

        var antwort = await client.PostAsync($"/api/v1/rezepte/{id}/entwurf", null, _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(antwort)).ShouldBe("recht.fehlt");
    }

    [Fact]
    public async Task Inhaber_darf_alles_vom_Anlegen_bis_zum_Verwerfen()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.InhaberA);
        var id = await AnlegenAlsAsync(Persona.InhaberA, freigeben: true);

        (await client.PostAsync($"/api/v1/rezepte/{id}/entwurf", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await client.PutAsJsonAsync($"/api/v1/rezepte/{id}/entwurf", new { zutaten = new[] { new { bezeichnung = "Kuvertüre", gramm = 150m } } }, _ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/v1/rezepte/{id}/entwurf", _ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=500", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- (2) Betriebe ----------

    [Fact]
    public async Task Betrieb_im_Anfragekoerper_wird_ignoriert_es_zaehlt_nur_der_Token()
    {
        using var a = await factory.CreateClientAlsAsync(Persona.InhaberA);
        var antwort = await a.PostAsJsonAsync(
            "/api/v1/rezepte",
            new { name = "Untergeschoben", betriebId = "betrieb-b", betrieb = "betrieb-b", zutaten = new[] { new { bezeichnung = "Mehl", gramm = 100m } } },
            _ct);
        var id = antwort.StatusCode == HttpStatusCode.Created
            ? (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid()
            : Guid.Empty;
        using var b = await factory.CreateClientAlsAsync(Persona.InhaberB);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await b.GetAsync($"/api/v1/rezepte/{id}", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await a.GetAsync($"/api/v1/rezepte/{id}", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Fremder_Betrieb_mit_dem_echten_ETag_aendert_trotzdem_nichts()
    {
        // Der ETag ist kein Geheimnis (z. B. aus einem Log). Er darf den Zugriff auf fremde Rezepte nicht öffnen.
        var id = await AnlegenAlsAsync(Persona.InhaberA);
        using var a = await factory.CreateClientAlsAsync(Persona.InhaberA);
        var vorher = await a.GetAsync($"/api/v1/rezepte/{id}", _ct);
        var etag = vorher.Headers.ETag!;
        var inhaltVorher = await vorher.Content.ReadAsStringAsync(_ct);
        using var b = await factory.CreateClientAlsAsync(Persona.InhaberB);

        async Task<HttpResponseMessage> MitEtagAsync(HttpMethod methode, string pfad, HttpContent? inhalt = null)
        {
            using var anfrage = new HttpRequestMessage(methode, pfad) { Content = inhalt };
            anfrage.Headers.IfMatch.Add(etag);
            return await b.SendAsync(anfrage, _ct);
        }

        (await MitEtagAsync(HttpMethod.Put, $"/api/v1/rezepte/{id}/entwurf", JsonContent.Create(new { zutaten = new[] { new { bezeichnung = "Salz", gramm = 999m } } })))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MitEtagAsync(HttpMethod.Post, $"/api/v1/rezepte/{id}/entwurf/freigabe")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MitEtagAsync(HttpMethod.Delete, $"/api/v1/rezepte/{id}/entwurf")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MitEtagAsync(HttpMethod.Post, $"/api/v1/rezepte/{id}/entwurf")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var nachher = await a.GetAsync($"/api/v1/rezepte/{id}", _ct);
        nachher.Headers.ETag.ShouldBe(etag);
        (await nachher.Content.ReadAsStringAsync(_ct)).ShouldBe(inhaltVorher);
    }

    public static TheoryData<string> AlleNeunOperationen => new()
    {
        "GET /",
        "GET /fassungen/1",
        "GET /skaliert?zielgewichtGramm=900",
        "GET /skaliert/ring?durchmesserCm=18&hoeheCm=6",
        "GET /skaliert/blech?laengeCm=60&breiteCm=40&hoeheCm=2",
        "POST /entwurf",
        "PUT /entwurf",
        "POST /entwurf/freigabe",
        "DELETE /entwurf",
    };

    [Theory]
    [MemberData(nameof(AlleNeunOperationen))]
    public async Task Fremdes_Rezept_ist_bei_jeder_Operation_von_einem_nie_vorhandenen_nicht_zu_unterscheiden(string operation)
    {
        var idA = await AnlegenAlsAsync(Persona.InhaberA, freigeben: operation is "POST /entwurf");
        using var b = await factory.CreateClientAlsAsync(Persona.InhaberB);
        var (methode, pfad) = (operation.Split(' ')[0], operation.Split(' ')[1]);

        async Task<HttpResponseMessage> SendenAsync(Guid id)
        {
            using var anfrage = new HttpRequestMessage(new HttpMethod(methode), $"/api/v1/rezepte/{id}{pfad.TrimEnd('/')}");
            if (methode == "PUT")
            {
                anfrage.Content = JsonContent.Create(new { zutaten = new[] { new { bezeichnung = "Eier", gramm = 320m } } });
            }

            return await b.SendAsync(anfrage, _ct);
        }

        var fremd = await SendenAsync(idA);
        var nieVorhanden = await SendenAsync(Guid.NewGuid());

        fremd.StatusCode.ShouldBe(HttpStatusCode.NotFound, operation);
        nieVorhanden.StatusCode.ShouldBe(fremd.StatusCode, operation);
        fremd.Headers.ETag.ShouldBeNull(operation);
        (await OhneTraceIdAsync(fremd)).ShouldBe(await OhneTraceIdAsync(nieVorhanden), operation);
    }

    private static async Task<string> OhneTraceIdAsync(HttpResponseMessage antwort)
    {
        var text = await antwort.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(text))
        {
            return $"{antwort.Content.Headers.ContentType}|leer";
        }

        var inhalt = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text)!;
        inhalt.Remove("traceId");
        return $"{antwort.Content.Headers.ContentType}|{JsonSerializer.Serialize(inhalt.OrderBy(e => e.Key, StringComparer.Ordinal))}";
    }

    // ---------- (4) Grenzen ----------

    [Fact]
    public async Task Entwurf_mit_hundert_Zeilen_ist_erlaubt_mit_hunderteins_nicht()
    {
        var id = await AnlegenAlsAsync(Persona.KonditorA);
        using var client = await factory.CreateClientAlsAsync(Persona.KonditorA);
        static object Mit(int anzahl) => new { zutaten = Enumerable.Range(1, anzahl).Select(i => new { bezeichnung = $"Zutat {i}", gramm = 1.5m }) };

        (await client.PutAsJsonAsync($"/api/v1/rezepte/{id}/entwurf", Mit(100), _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var zuViel = await client.PutAsJsonAsync($"/api/v1/rezepte/{id}/entwurf", Mit(101), _ct);

        zuViel.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(zuViel)).ShouldBe("fassung.zu_viele_zutaten");
        var rezept = await client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}", _ct);
        rezept.GetProperty("zutaten").GetArrayLength().ShouldBe(100);
    }

    [Fact]
    public async Task Neuer_Entwurf_aus_einer_vollen_Fassung_mit_hundert_Zeilen_ist_moeglich()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.InhaberA);
        var angelegt = await client.PostAsJsonAsync(
            "/api/v1/rezepte",
            new { name = "Hochzeitstorte", zutaten = Enumerable.Range(1, 100).Select(i => new { bezeichnung = $"Zutat {i}", gramm = 2m }) },
            _ct);
        var id = (await angelegt.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();
        (await client.PostAsync($"/api/v1/rezepte/{id}/entwurf/freigabe", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var entwurf = await client.PostAsync($"/api/v1/rezepte/{id}/entwurf", null, _ct);

        entwurf.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await entwurf.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("zutaten").GetArrayLength().ShouldBe(100);
    }

    private static StringContent AnfrageMitGroesse(int bytes)
    {
        // Gültiges JSON mit genau der gewünschten Größe in Bytes (ASCII). Der Name ist absichtlich zu lang (422), damit
        // sichtbar wird, dass die Anfrage bis zur Fachprüfung kommt und nicht an der Größe scheitert.
        const string vorne = """{"name":" """;
        const string hinten = """ ","zutaten":[]}""";
        var fuellung = bytes - Encoding.UTF8.GetByteCount(vorne.TrimEnd()) - Encoding.UTF8.GetByteCount(hinten.TrimStart());
        var json = vorne.TrimEnd() + new string('x', fuellung) + hinten.TrimStart();
        Encoding.UTF8.GetByteCount(json).ShouldBe(bytes);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    [Fact]
    public async Task Anfrage_von_genau_1_MB_wird_nicht_wegen_der_Groesse_abgewiesen()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.InhaberA);

        var antwort = await client.PostAsync("/api/v1/rezepte", AnfrageMitGroesse(1_000_000), _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Anfrage_ein_Byte_ueber_1_MB_wird_mit_413_abgewiesen()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.InhaberA);

        var antwort = await client.PostAsync("/api/v1/rezepte", AnfrageMitGroesse(1_000_001), _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Uebergrosser_Entwurf_wird_mit_413_als_Problem_Details_abgewiesen()
    {
        var id = await AnlegenAlsAsync(Persona.InhaberA);
        using var client = await factory.CreateClientAlsAsync(Persona.InhaberA);
        var riesig = new
        {
            zutaten = Enumerable.Range(1, 60).Select(i => new { bezeichnung = new string('z', 20_000), gramm = 1m }),
        };

        // Mit Längenangabe; ohne Längenangabe greift die Kestrel-Grenze (siehe AnfragegroesseKestrelTests),
        // die der In-Memory-TestServer nicht kennt.
        var antwort = await client.PutAsync(
            $"/api/v1/rezepte/{id}/entwurf",
            new StringContent(JsonSerializer.Serialize(riesig), Encoding.UTF8, "application/json"),
            _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        antwort.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }
}
