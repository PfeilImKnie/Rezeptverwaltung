using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>
/// Startet die komplette API im Speicher gegen eine echte PostgreSQL (eigener Container pro Testklasse).
/// Die API migriert die Datenbank beim Start selbst. Damit ist auch dieser Pfad getestet.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _datenbank = new PostgreSqlBuilder("docker.io/library/postgres:18-alpine").Build();

    public async ValueTask InitializeAsync() => await _datenbank.StartAsync();

    /// <summary>Client, der bei Änderungen am Entwurf automatisch den aktuellen Stand als If-Match mitsendet.</summary>
    public HttpClient CreateClientMitAktuellemStand() => CreateDefaultClient(new IfMatchAutomatik());

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:rezeptverwaltung", _datenbank.GetConnectionString());

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _datenbank.DisposeAsync();
    }
}
