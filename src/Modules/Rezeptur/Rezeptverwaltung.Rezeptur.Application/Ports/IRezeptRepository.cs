using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Application.Ports;

/// <summary>Ausgehender Port: Persistenz für das Aggregat <see cref="Rezept"/>.</summary>
public interface IRezeptRepository
{
    Task HinzufuegenAsync(Rezept rezept, CancellationToken cancellationToken);

    Task<Rezept?> LadenAsync(RezeptId id, CancellationToken cancellationToken);
}
