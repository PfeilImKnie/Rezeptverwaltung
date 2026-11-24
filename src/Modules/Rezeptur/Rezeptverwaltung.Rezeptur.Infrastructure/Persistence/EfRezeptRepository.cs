using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;

internal sealed class EfRezeptRepository(RezepturDbContext datenbank) : IRezeptRepository
{
    public async Task HinzufuegenAsync(Rezept rezept, CancellationToken cancellationToken)
    {
        // Verteidigung in der Tiefe (ADR 0015): nur Rezepte des eigenen Betriebs anlegen.
        if (datenbank.AktuellerBetrieb is not { } betrieb || rezept.BetriebId != betrieb)
        {
            throw new InvalidOperationException("Ein Rezept darf nur im Betrieb der angemeldeten Person angelegt werden.");
        }

        datenbank.Rezepte.Add(rezept);
        await datenbank.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<Rezept?> LadenAsync(RezeptId id, CancellationToken cancellationToken) =>
        datenbank.Rezepte.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public string StandVon(Rezept rezept) =>
        datenbank.Entry(rezept).Property<uint>(RezeptKonfiguration.Stand).CurrentValue.ToString(CultureInfo.InvariantCulture);

    public async Task SpeichernAsync(Rezept rezept, CancellationToken cancellationToken)
    {
        try
        {
            await datenbank.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ausnahme)
        {
            // Zwei Geräte (z. B. Tablet und Alexa) haben dasselbe Rezept gleichzeitig geändert.
            throw new DomainRuleViolationException(
                "rezept.gleichzeitig_geaendert",
                "Das Rezept wurde inzwischen an anderer Stelle geändert. Bitte neu laden und erneut versuchen.",
                Regelart.Zustandskonflikt,
                ausnahme);
        }
    }
}
