using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>Ein PostgreSQL-Container, den mehrere API-Instanzen nacheinander nutzen (Neustart-Szenarien).</summary>
public sealed class GemeinsameDatenbank : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("docker.io/library/postgres:18-alpine").Build();

    public string Verbindung => _container.GetConnectionString();

    /// <summary>Eigene, leere Datenbank im selben Container (noch nicht angelegt; die API legt sie beim Migrieren an).</summary>
    public string NeueDatenbank() =>
        new NpgsqlConnectionStringBuilder(Verbindung) { Database = $"api_{Guid.NewGuid():N}" }.ConnectionString;

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

/// <summary>Eine API-Instanz mit frei wählbarer Konfiguration. Jede Instanz entspricht einem Start der API.</summary>
internal sealed class KonfigurierteApi(IReadOnlyDictionary<string, string?> einstellungen) : WebApplicationFactory<Program>
{
    // Seit ADR 0015 braucht jede API-Instanz einen OIDC-Aussteller und Clients einen Token. Die Einstellungen
    // des Tests überschreiben die Vorgaben (UseSetting: letzter Wert gewinnt).
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestLogin.StartenAsync().GetAwaiter().GetResult();
        foreach (var (schluessel, wert) in TestLogin.Einstellungen)
        {
            builder.UseSetting(schluessel, wert);
        }

        foreach (var (schluessel, wert) in einstellungen)
        {
            builder.UseSetting(schluessel, wert);
        }
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = TestLogin.Header(TestLogin.TokenAsync(Persona.InhaberA).GetAwaiter().GetResult());
    }

    public static KonfigurierteApi Mit(string verbindung, params (string Schluessel, string? Wert)[] weitere) =>
        new(new Dictionary<string, string?>(weitere.ToDictionary(x => x.Schluessel, x => x.Wert))
        {
            ["ConnectionStrings:rezeptverwaltung"] = verbindung,
        });
}

/// <summary>
/// Unabhängiger Test der Akzeptanzkriterien 1, 2, 6 und 7 Ende-zu-Ende über HTTP und eine echte PostgreSQL.
/// </summary>
public sealed class PersistenzApiTests(GemeinsameDatenbank datenbank) : IClassFixture<GemeinsameDatenbank>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static readonly object Sachermasse = new
    {
        name = "Sachermasse Ø 26 – „Hotel“",
        zutaten = new object[]
        {
            new { bezeichnung = "Butter", gramm = 140 },
            new { bezeichnung = "Staubzucker", gramm = 30 },
            new { bezeichnung = "Eigelb", gramm = 120 },
            new { bezeichnung = "Kuvertüre", gramm = 140.55m },
            new { bezeichnung = "Eiklar", gramm = 180 },
            new { bezeichnung = "Kristallzucker", gramm = 110 },
            new { bezeichnung = "Mehl", gramm = 140 },
            new { bezeichnung = "Salz", gramm = 0.01m },
            new { bezeichnung = "Staubzucker", gramm = 30 },
        },
    };

    private static string Zutaten(JsonElement rezept) =>
        string.Join(" | ", rezept.GetProperty("zutaten").EnumerateArray()
            .Select(z => string.Create(
                CultureInfo.InvariantCulture,
                $"{z.GetProperty("bezeichnung").GetString()}={z.GetProperty("gramm").GetDecimal()}")));

    [Fact]
    public async Task Rezept_ueberlebt_einen_Neustart_der_API_vollstaendig()
    {
        var verbindung = datenbank.NeueDatenbank();
        JsonElement angelegt;
        await using (var ersterStart = KonfigurierteApi.Mit(verbindung))
        {
            var antwort = await ersterStart.CreateClient().PostAsJsonAsync("/api/v1/rezepte", Sachermasse, _ct);
            antwort.StatusCode.ShouldBe(HttpStatusCode.Created);
            angelegt = await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct);
        }

        await using var zweiterStart = KonfigurierteApi.Mit(verbindung);
        var id = angelegt.GetProperty("id").GetGuid();
        var geladen = await zweiterStart.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}", _ct);

        geladen.GetProperty("id").GetGuid().ShouldBe(id);
        geladen.GetProperty("name").GetString().ShouldBe("Sachermasse Ø 26 – „Hotel“");
        Zutaten(geladen).ShouldBe(
            "Butter=140 | Staubzucker=30 | Eigelb=120 | Kuvertüre=140.55 | Eiklar=180 | Kristallzucker=110 | Mehl=140 | Salz=0.01 | Staubzucker=30");
        geladen.GetProperty("gesamtgewichtGramm").GetDecimal().ShouldBe(890.56m);
    }

    [Fact]
    public async Task Nach_dem_Neustart_wird_das_gespeicherte_Rezept_skaliert()
    {
        // Backstube: Mürbeteig 1-2-3 am Vortag erfasst, heute auf 1,2 kg umrechnen.
        var verbindung = datenbank.NeueDatenbank();
        Guid id;
        await using (var gestern = KonfigurierteApi.Mit(verbindung))
        {
            var antwort = await gestern.CreateClient().PostAsJsonAsync(
                "/api/v1/rezepte",
                new
                {
                    name = "Mürbeteig 1-2-3",
                    zutaten = new object[]
                    {
                        new { bezeichnung = "Zucker", gramm = 100 },
                        new { bezeichnung = "Butter", gramm = 200 },
                        new { bezeichnung = "Mehl", gramm = 300 },
                    },
                },
                _ct);
            id = (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();
        }

        await using var heute = KonfigurierteApi.Mit(verbindung);
        var skaliert = await heute.CreateClient()
            .GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=1200", _ct);

        Zutaten(skaliert).ShouldBe("Zucker=200 | Butter=400 | Mehl=600");
    }

    [Fact]
    public async Task Leeres_Rezept_bleibt_nach_dem_Neustart_leer_und_liefert_beim_Skalieren_409()
    {
        var verbindung = datenbank.NeueDatenbank();
        Guid id;
        await using (var ersterStart = KonfigurierteApi.Mit(verbindung))
        {
            var antwort = await ersterStart.CreateClient().PostAsJsonAsync(
                "/api/v1/rezepte", new { name = "Noch leer", zutaten = Array.Empty<object>() }, _ct);
            id = (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();
        }

        await using var zweiterStart = KonfigurierteApi.Mit(verbindung);
        var client = zweiterStart.CreateClient();

        var geladen = await client.GetFromJsonAsync<JsonElement>($"/api/v1/rezepte/{id}", _ct);
        geladen.GetProperty("zutaten").GetArrayLength().ShouldBe(0);
        geladen.GetProperty("gesamtgewichtGramm").GetDecimal().ShouldBe(0m);

        var skalieren = await client.GetAsync($"/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=100", _ct);
        skalieren.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await skalieren.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("code").GetString().ShouldBe("rezept.ohne_zutaten");
    }

    [Fact]
    public async Task API_legt_das_Schema_rezeptur_beim_Start_in_einer_leeren_Datenbank_an()
    {
        var verbindung = datenbank.NeueDatenbank();

        await using (var api = KonfigurierteApi.Mit(verbindung))
        {
            (await api.CreateClient().GetAsync("/health", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await TabellenImSchemaRezepturAsync(verbindung)).ShouldContain("rezepte");
    }

    [Fact]
    public async Task Abgeschaltete_Migration_beim_Start_legt_kein_Schema_an()
    {
        // Nicht gefordertes Zusatzverhalten (Schalter Persistenz:MigrationenBeimStart): prüfen, dass er wirkt.
        var verbindung = datenbank.NeueDatenbank();
        await using (var anlegen = new NpgsqlConnection(datenbank.Verbindung))
        {
            await anlegen.OpenAsync(_ct);
            var name = new NpgsqlConnectionStringBuilder(verbindung).Database;
            await using var befehl = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", anlegen);
            await befehl.ExecuteNonQueryAsync(_ct);
        }

        await using (var api = KonfigurierteApi.Mit(verbindung, ("Persistenz:MigrationenBeimStart", "false")))
        {
            _ = api.CreateClient();
        }

        (await TabellenImSchemaRezepturAsync(verbindung)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ohne_Verbindungszeichenfolge_bricht_der_Start_mit_verstaendlicher_Meldung_ab(string? verbindung)
    {
        using var api = new KonfigurierteApi(verbindung is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["ConnectionStrings:rezeptverwaltung"] = verbindung });

        var fehler = Should.Throw<Exception>(() => api.CreateClient());

        var meldungen = string.Join(" / ", Kette(fehler).Select(f => f.Message));
        meldungen.ShouldContain("ConnectionStrings:rezeptverwaltung", Case.Sensitive, $"Meldung war: {meldungen}");
        meldungen.ShouldContain("AppHost", Case.Sensitive, $"Meldung war: {meldungen}");
    }

    [Theory]
    [InlineData("Torte\\u0000Spezial")]
    [InlineData("\\u0000Torte")]
    public async Task Name_mit_Nullzeichen_wird_mit_422_abgewiesen_statt_500(string jsonName)
    {
        // Gefunden vom unabhängigen Test: PostgreSQL kann U+0000 nicht speichern, das führte zu 500.
        // Entscheidung (ADR 0011): U+0000 ist in Vertrag und Domain unzulässig. Ursprüngliche Erwartung war 201.
        await using var api = KonfigurierteApi.Mit(datenbank.NeueDatenbank());
        var json = $$"""{"name":"{{jsonName}}","zutaten":[{"bezeichnung":"Mehl","gramm":100}]}""";

        var antwort = await api.CreateClient().PostAsync("/api/v1/rezepte", new StringContent(json, Encoding.UTF8, "application/json"), _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("code").GetString().ShouldBe("rezept.name_nullzeichen");
    }

    [Fact]
    public async Task Zutatenbezeichnung_mit_Nullzeichen_fuehrt_nicht_zu_500()
    {
        await using var api = KonfigurierteApi.Mit(datenbank.NeueDatenbank());
        const string json = """{"name":"Biskuit","zutaten":[{"bezeichnung":"Mehl\u0000","gramm":100}]}""";

        var antwort = await api.CreateClient()
            .PostAsync("/api/v1/rezepte", new StringContent(json, Encoding.UTF8, "application/json"), _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await antwort.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("code").GetString().ShouldBe("zutat.bezeichnung_nullzeichen");
    }

    [Fact]
    public async Task Ungepaartes_Surrogat_im_Namen_fuehrt_nicht_zu_500()
    {
        await using var api = KonfigurierteApi.Mit(datenbank.NeueDatenbank());
        const string json = """{"name":"Torte \ud800","zutaten":[{"bezeichnung":"Mehl","gramm":100}]}""";

        var antwort = await api.CreateClient()
            .PostAsync("/api/v1/rezepte", new StringContent(json, Encoding.UTF8, "application/json"), _ct);

        ((int)antwort.StatusCode).ShouldBeLessThan(500);
    }

    private static IEnumerable<Exception> Kette(Exception fehler)
    {
        for (var aktuell = fehler; aktuell is not null; aktuell = aktuell.InnerException)
        {
            yield return aktuell;
            if (aktuell is AggregateException sammlung)
            {
                foreach (var inner in sammlung.InnerExceptions.SelectMany(Kette))
                {
                    yield return inner;
                }
            }
        }
    }

    private async Task<List<string>> TabellenImSchemaRezepturAsync(string verbindung)
    {
        await using var db = new NpgsqlConnection(verbindung);
        await db.OpenAsync(_ct);
        await using var befehl = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'rezeptur'", db);
        await using var leser = await befehl.ExecuteReaderAsync(_ct);
        var tabellen = new List<string>();
        while (await leser.ReadAsync(_ct))
        {
            tabellen.Add(leser.GetString(0));
        }

        return tabellen;
    }
}
