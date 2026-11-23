using Microsoft.EntityFrameworkCore;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;

/// <summary>Datenbankzugriff des Moduls „Rezeptur“. Eigenes Schema, eigene Migrationen (ADR 0008).</summary>
internal sealed class RezepturDbContext(DbContextOptions<RezepturDbContext> options) : DbContext(options)
{
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
        modelBuilder.ApplyConfiguration(new RezeptKonfiguration());
    }
}
