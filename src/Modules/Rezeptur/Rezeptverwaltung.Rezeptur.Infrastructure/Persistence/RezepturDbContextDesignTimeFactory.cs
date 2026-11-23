using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;

/// <summary>
/// Nur für <c>dotnet ef migrations add</c>. Baut kein Datenbank-Schema auf, braucht also keine laufende Datenbank.
/// </summary>
internal sealed class RezepturDbContextDesignTimeFactory : IDesignTimeDbContextFactory<RezepturDbContext>
{
    public RezepturDbContext CreateDbContext(string[] args)
    {
        var optionen = new DbContextOptionsBuilder<RezepturDbContext>();
        RezepturDbContext.Konfiguriere(optionen, "Host=localhost;Database=rezeptverwaltung_designtime");
        return new RezepturDbContext(optionen.Options, OhneAnmeldung.Instanz);
    }
}

/// <summary>Keine angemeldete Person: kein Betrieb, keine Rollen. Für Design-Time und Migrationen beim Start.</summary>
internal sealed class OhneAnmeldung : IAktuellerBenutzer
{
    public static readonly OhneAnmeldung Instanz = new();

    public BetriebId? Betrieb => null;

    public IReadOnlySet<Rolle> Rollen { get; } = new HashSet<Rolle>();

    public bool BetriebMehrdeutig => false;
}
