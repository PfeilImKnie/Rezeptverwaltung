using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

public sealed record RezeptAnlegen(string Name, IReadOnlyList<ZutatEingabe> Zutaten);

public sealed record ZutatEingabe(string Bezeichnung, decimal Gramm)
{
    internal static IReadOnlyList<Zutatenzeile> AlsZutatenzeilen(IEnumerable<ZutatEingabe> eingaben) =>
        eingaben.Select(z => Zutatenzeile.Von(z.Bezeichnung, Gewicht.Erfasst(z.Gramm))).ToList();
}

public sealed class RezeptAnlegenHandler(IRezeptRepository repository, TimeProvider zeit)
{
    public async Task<RezeptDetails> HandleAsync(RezeptAnlegen befehl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(befehl);

        var rezept = Rezept.Anlegen(
            RezeptName.Von(befehl.Name),
            ZutatEingabe.AlsZutatenzeilen(befehl.Zutaten),
            zeit.GetUtcNow());

        await repository.HinzufuegenAsync(rezept, cancellationToken).ConfigureAwait(false);
        return RezeptDetails.Aus(rezept, repository.StandVon(rezept));
    }
}
