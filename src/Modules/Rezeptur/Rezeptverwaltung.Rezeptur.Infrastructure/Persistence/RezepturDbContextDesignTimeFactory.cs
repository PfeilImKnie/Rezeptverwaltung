using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

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
        return new RezepturDbContext(optionen.Options);
    }
}
