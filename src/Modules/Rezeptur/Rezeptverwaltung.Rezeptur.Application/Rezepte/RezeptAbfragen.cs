using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

public sealed class RezeptAbfragenHandler(IRezeptRepository repository, IAktuellerBenutzer benutzer)
{
    public async Task<RezeptDetails?> HandleAsync(Guid rezeptId, CancellationToken cancellationToken)
    {
        var betrieb = ZugriffVerweigertException.Pruefen(benutzer, Rechte.Lesen, "Rezept lesen");
        var rezept = Rechte.NurEigenes(await repository.LadenAsync(new RezeptId(rezeptId), cancellationToken).ConfigureAwait(false), betrieb);
        return rezept is null ? null : RezeptDetails.Aus(rezept, repository.StandVon(rezept));
    }
}
