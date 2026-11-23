using System.Globalization;
using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Application.Tests;

/// <summary>Test-Fake für den Persistenz-Port. Der Stand zählt jede gespeicherte Änderung hoch (wie xmin in der DB).</summary>
internal sealed class InMemoryRezeptRepositoryFake : IRezeptRepository
{
    private readonly Dictionary<RezeptId, int> _staende = [];

    public Dictionary<RezeptId, Rezept> Gespeichert { get; } = [];

    public int Speichervorgaenge { get; private set; }

    public Task HinzufuegenAsync(Rezept rezept, CancellationToken cancellationToken)
    {
        Gespeichert.Add(rezept.Id, rezept);
        _staende[rezept.Id] = 1;
        return Task.CompletedTask;
    }

    public Task<Rezept?> LadenAsync(RezeptId id, CancellationToken cancellationToken) =>
        Task.FromResult(Gespeichert.GetValueOrDefault(id));

    public Task SpeichernAsync(Rezept rezept, CancellationToken cancellationToken)
    {
        Speichervorgaenge++;
        _staende[rezept.Id]++;
        return Task.CompletedTask;
    }

    public string StandVon(Rezept rezept) => _staende[rezept.Id].ToString(CultureInfo.InvariantCulture);

    /// <summary>Aktueller Stand, wie ihn ein Client aus dem letzten ETag kennt; für unbekannte Rezepte ein beliebiger Wert.</summary>
    public string Stand(Guid rezeptId) =>
        _staende.TryGetValue(new RezeptId(rezeptId), out var stand) ? stand.ToString(CultureInfo.InvariantCulture) : "0";
}
