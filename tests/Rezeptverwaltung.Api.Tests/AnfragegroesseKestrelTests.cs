using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>
/// API auf echtem Kestrel (nicht im In-Memory-TestServer), damit die Server-Grenze für Anfragen ohne Längenangabe
/// (chunked) wirklich greift. Die Datenbank wird nicht gebraucht: Alle Anfragen scheitern vorher.
/// </summary>
public sealed class KestrelApi : WebApplicationFactory<Program>, IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await TestLogin.StartenAsync();
        UseKestrel(0);
        StartServer();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (schluessel, wert) in TestLogin.Einstellungen)
        {
            builder.UseSetting(schluessel, wert);
        }

        builder.UseSetting("ConnectionStrings:rezeptverwaltung", "Host=127.0.0.1;Port=1;Database=nicht_benutzt;Username=x;Password=x");
        builder.UseSetting("Persistenz:MigrationenBeimStart", "false");
    }

    public override async ValueTask DisposeAsync() => await base.DisposeAsync();
}

/// <summary>Anfragen über 1 MB werden abgewiesen (Issue #6), auch ohne Content-Length.</summary>
public sealed class AnfragegroesseKestrelTests(KestrelApi api) : IClassFixture<KestrelApi>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Inhalt ohne bekannte Länge: HttpClient sendet ihn chunked.</summary>
    private sealed class OhneLaenge(byte[] daten) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(daten).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    private async Task<HttpResponseMessage> SendenAsync(string json)
    {
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = TestLogin.Header(await TestLogin.TokenAsync(Persona.InhaberA));
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, "/api/v1/rezepte")
        {
            Content = new OhneLaenge(Encoding.UTF8.GetBytes(json)),
        };
        anfrage.Content.Headers.ContentType = new("application/json");
        anfrage.Headers.TransferEncodingChunked = true;
        return await client.SendAsync(anfrage, _ct);
    }

    [Fact]
    public async Task Kleine_Anfrage_ohne_Laengenangabe_erreicht_die_API()
    {
        // Kontrolle: Der Aufbau funktioniert; kaputtes JSON ergibt 400, nicht 401 oder 413.
        var antwort = await SendenAsync("{");

        antwort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Uebergrosse_Anfrage_ohne_Laengenangabe_wird_mit_413_abgewiesen()
    {
        var antwort = await SendenAsync($$"""{"name":"{{new string('x', 1_100_000)}}","zutaten":[]}""");

        antwort.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Uebergrosse_Anfrage_mit_Laengenangabe_wird_auch_auf_Kestrel_mit_413_abgewiesen()
    {
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = TestLogin.Header(await TestLogin.TokenAsync(Persona.InhaberA));

        var antwort = await client.PostAsync(
            "/api/v1/rezepte",
            new StringContent($$"""{"name":"{{new string('x', 1_100_000)}}","zutaten":[]}""", Encoding.UTF8, "application/json"),
            _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        antwort.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }
}
