using Microsoft.EntityFrameworkCore;
using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;

internal sealed class EfRezeptRepository(RezepturDbContext datenbank) : IRezeptRepository
{
    public async Task HinzufuegenAsync(Rezept rezept, CancellationToken cancellationToken)
    {
        datenbank.Rezepte.Add(rezept);
        await datenbank.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<Rezept?> LadenAsync(RezeptId id, CancellationToken cancellationToken) =>
        datenbank.Rezepte.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
}
