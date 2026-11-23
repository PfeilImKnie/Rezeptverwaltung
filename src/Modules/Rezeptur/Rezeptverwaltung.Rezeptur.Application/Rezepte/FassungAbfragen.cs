using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

public sealed class FassungAbfragenHandler(IRezeptRepository repository)
{
    /// <returns><c>null</c>, wenn es das Rezept oder die Fassung nicht gibt.</returns>
    public async Task<FassungDetails?> HandleAsync(Guid rezeptId, int nummer, CancellationToken cancellationToken)
    {
        var rezept = await repository.LadenAsync(new RezeptId(rezeptId), cancellationToken).ConfigureAwait(false);
        var fassung = rezept?.FassungMitNummer(nummer);
        return fassung is null ? null : FassungDetails.Aus(rezept!, fassung, repository.StandVon(rezept!));
    }
}
