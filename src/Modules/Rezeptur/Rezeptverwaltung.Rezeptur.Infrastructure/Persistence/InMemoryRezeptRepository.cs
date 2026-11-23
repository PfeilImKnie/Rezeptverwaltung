using System.Collections.Concurrent;
using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;

/// <summary>
/// Übergangslösung bis zur PostgreSQL-Anbindung (siehe ADR 0008). Daten gehen beim Neustart verloren.
/// </summary>
internal sealed class InMemoryRezeptRepository : IRezeptRepository
{
    private readonly ConcurrentDictionary<RezeptId, Rezept> _rezepte = new();

    public Task HinzufuegenAsync(Rezept rezept, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rezept);
        if (!_rezepte.TryAdd(rezept.Id, rezept))
        {
            throw new InvalidOperationException($"Rezept {rezept.Id} existiert bereits.");
        }

        return Task.CompletedTask;
    }

    public Task<Rezept?> LadenAsync(RezeptId id, CancellationToken cancellationToken) =>
        Task.FromResult(_rezepte.GetValueOrDefault(id));
}
