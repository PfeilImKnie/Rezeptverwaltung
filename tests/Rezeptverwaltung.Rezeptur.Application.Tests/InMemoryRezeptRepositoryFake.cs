using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Application.Tests;

/// <summary>Test-Fake für den Persistenz-Port. Use Cases werden ohne Infrastruktur getestet.</summary>
internal sealed class InMemoryRezeptRepositoryFake : IRezeptRepository
{
    public Dictionary<RezeptId, Rezept> Gespeichert { get; } = [];

    public Task HinzufuegenAsync(Rezept rezept, CancellationToken cancellationToken)
    {
        Gespeichert.Add(rezept.Id, rezept);
        return Task.CompletedTask;
    }

    public Task<Rezept?> LadenAsync(RezeptId id, CancellationToken cancellationToken) =>
        Task.FromResult(Gespeichert.GetValueOrDefault(id));
}
