using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

public sealed record RezeptAnlegen(string Name, IReadOnlyList<ZutatEingabe> Zutaten, Form? Bezugsform = null);

public sealed record ZutatEingabe(string Bezeichnung, decimal Gramm)
{
    internal static IReadOnlyList<Zutatenzeile> AlsZutatenzeilen(IEnumerable<ZutatEingabe> eingaben) =>
        eingaben.Select(z => Zutatenzeile.Von(z.Bezeichnung, Gewicht.Erfasst(z.Gramm))).ToList();
}

public sealed class RezeptAnlegenHandler(IRezeptRepository repository, TimeProvider zeit, IAktuellerBenutzer benutzer)
{
    public async Task<RezeptDetails> HandleAsync(RezeptAnlegen befehl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(befehl);
        var betrieb = ZugriffVerweigertException.Pruefen(benutzer, Rechte.Bearbeiten, "Rezept anlegen");

        var rezept = Rezept.Anlegen(
            betrieb,
            RezeptName.Von(befehl.Name),
            ZutatEingabe.AlsZutatenzeilen(befehl.Zutaten),
            zeit.GetUtcNow(),
            befehl.Bezugsform?.AlsBezugsform());

        await repository.HinzufuegenAsync(rezept, cancellationToken).ConfigureAwait(false);
        return RezeptDetails.Aus(rezept, repository.StandVon(rezept));
    }
}
