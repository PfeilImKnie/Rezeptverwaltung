using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

public sealed record RezeptSkalieren(Guid RezeptId, decimal ZielgewichtGramm);

public sealed record SkaliertesRezept(Guid RezeptId, string Name, decimal ZielgewichtGramm, IReadOnlyList<ZutatMenge> Zutaten);

public sealed class RezeptSkalierenHandler(IRezeptRepository repository)
{
    public async Task<SkaliertesRezept?> HandleAsync(RezeptSkalieren abfrage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(abfrage);

        var rezept = await repository.LadenAsync(new RezeptId(abfrage.RezeptId), cancellationToken).ConfigureAwait(false);
        if (rezept is null)
        {
            return null;
        }

        var zielgewicht = Gewicht.Erfasst(abfrage.ZielgewichtGramm);
        var zutaten = rezept.SkaliertAuf(zielgewicht).Select(ZutatMenge.Aus).ToList();
        return new SkaliertesRezept(rezept.Id.Wert, rezept.Name.Wert, zielgewicht.Gramm, zutaten);
    }
}
