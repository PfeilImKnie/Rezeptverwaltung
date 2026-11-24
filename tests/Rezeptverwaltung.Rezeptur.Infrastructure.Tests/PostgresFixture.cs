using Microsoft.EntityFrameworkCore;
using Npgsql;
using Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;
using Rezeptverwaltung.SharedKernel;
using Testcontainers.PostgreSql;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Tests;

/// <summary>
/// Echte PostgreSQL-Instanz (gleiche Hauptversion wie im Betrieb) pro Testklasse.
/// Lokal über Podman oder Docker, siehe README „Voraussetzungen → Einmalig mit Podman“ (ADR 0012).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "docker.io/library/postgres:18-alpine";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();

    public string Verbindung => _container.GetConnectionString();

    /// <summary>Verbindung zu einer eigenen, noch nicht existierenden Datenbank im selben Container.</summary>
    public string NeueDatenbank() =>
        new NpgsqlConnectionStringBuilder(Verbindung) { Database = $"test_{Guid.NewGuid():N}" }.ConnectionString;

    /// <summary>Kontext für die angemeldete Person (Standard: Inhaber/in von Betrieb A).</summary>
    internal RezepturDbContext NeuerKontext(string? verbindung = null, IAktuellerBenutzer? benutzer = null)
    {
        var optionen = new DbContextOptionsBuilder<RezepturDbContext>();
        RezepturDbContext.Konfiguriere(optionen, verbindung ?? Verbindung);
        return new RezepturDbContext(optionen.Options, benutzer ?? Testbenutzer.InhaberA);
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var kontext = NeuerKontext();
        await kontext.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
