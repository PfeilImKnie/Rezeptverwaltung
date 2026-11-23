using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

public sealed class FassungAbfragenHandler(IRezeptRepository repository, IAktuellerBenutzer benutzer)
{
    /// <returns><c>null</c>, wenn es das Rezept oder die Fassung nicht gibt.</returns>
    public async Task<FassungDetails?> HandleAsync(Guid rezeptId, int nummer, CancellationToken cancellationToken)
    {
        var betrieb = ZugriffVerweigertException.Pruefen(benutzer, Rechte.Lesen, "Fassung lesen");
        var rezept = Rechte.NurEigenes(await repository.LadenAsync(new RezeptId(rezeptId), cancellationToken).ConfigureAwait(false), betrieb);
        var fassung = rezept?.FassungMitNummer(nummer);
        return fassung is null ? null : FassungDetails.Aus(rezept!, fassung, repository.StandVon(rezept!));
    }
}
