using Microsoft.EntityFrameworkCore;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;

/// <summary>
/// Datenbankzugriff des Moduls „Rezeptur“. Eigenes Schema, eigene Migrationen (ADR 0008).
/// Jede Abfrage ist auf den Betrieb der angemeldeten Person gefiltert (ADR 0015); ohne Betrieb liefert sie nichts.
/// </summary>
internal sealed class RezepturDbContext(DbContextOptions<RezepturDbContext> options, IAktuellerBenutzer benutzer) : DbContext(options)
{
    /// <summary>Vom Mandantenfilter je Abfrage ausgewertet (EF parametrisiert Kontext-Eigenschaften).</summary>
    internal BetriebId? AktuellerBetrieb => benutzer.Betrieb;

    public const string Schema = "rezeptur";

    public DbSet<Rezept> Rezepte => Set<Rezept>();

    /// <summary>Gemeinsame Npgsql-Konfiguration für Laufzeit und Design-Time (Migrationen).</summary>
    public static void Konfiguriere(DbContextOptionsBuilder optionen, string verbindungszeichenfolge) =>
        optionen.UseNpgsql(
            verbindungszeichenfolge,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrationen", Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new RezeptKonfiguration(this));
    }
}
