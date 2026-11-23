using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

public sealed class RezeptAbfragenHandler(IRezeptRepository repository)
{
    public async Task<RezeptDetails?> HandleAsync(Guid rezeptId, CancellationToken cancellationToken)
    {
        var rezept = await repository.LadenAsync(new RezeptId(rezeptId), cancellationToken).ConfigureAwait(false);
        return rezept is null ? null : RezeptDetails.Aus(rezept);
    }
}
