using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>
/// Startet die komplette API im Speicher gegen eine echte PostgreSQL (eigener Container pro Testklasse) und einen
/// echten OIDC-Aussteller (TestLogin). Clients sind standardmäßig als Inhaber/in von Betrieb A angemeldet.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _datenbank = new PostgreSqlBuilder("docker.io/library/postgres:18-alpine").Build();
    private string? _standardToken;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_datenbank.StartAsync(), TestLogin.StartenAsync());
        _standardToken = await TestLogin.TokenAsync(Persona.InhaberA);
    }

    /// <summary>Client, der bei Änderungen am Entwurf automatisch den aktuellen Stand als If-Match mitsendet.</summary>
    public HttpClient CreateClientMitAktuellemStand() => CreateDefaultClient(new IfMatchAutomatik());

    /// <summary>Client ohne Anmeldung.</summary>
    public HttpClient CreateClientOhneAnmeldung()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    /// <summary>Client, angemeldet als die angegebene Testperson.</summary>
    public async Task<HttpClient> CreateClientAlsAsync(Persona persona)
    {
        var client = CreateDefaultClient(new IfMatchAutomatik());
        client.DefaultRequestHeaders.Authorization = TestLogin.Header(await TestLogin.TokenAsync(persona));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:rezeptverwaltung", _datenbank.GetConnectionString());
        foreach (var (schluessel, wert) in TestLogin.Einstellungen)
        {
            builder.UseSetting(schluessel, wert);
        }
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = TestLogin.Header(_standardToken!);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _datenbank.DisposeAsync();
    }
}
