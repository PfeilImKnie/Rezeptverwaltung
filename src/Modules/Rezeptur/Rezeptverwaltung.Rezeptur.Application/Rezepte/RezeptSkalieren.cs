using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

/// <param name="Fassung">Nummer der Fassung; <c>null</c> = maßgebliche Fassung.</param>
public sealed record RezeptSkalieren(Guid RezeptId, decimal ZielgewichtGramm, int? Fassung = null);

public sealed record SkaliertesRezept(Guid RezeptId, string Name, int Fassung, decimal ZielgewichtGramm, IReadOnlyList<ZutatMenge> Zutaten);

public sealed class RezeptSkalierenHandler(IRezeptRepository repository)
{
    /// <returns><c>null</c>, wenn es das Rezept oder die angegebene Fassung nicht gibt.</returns>
    public async Task<SkaliertesRezept?> HandleAsync(RezeptSkalieren abfrage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(abfrage);

        var rezept = await repository.LadenAsync(new RezeptId(abfrage.RezeptId), cancellationToken).ConfigureAwait(false);
        var fassung = abfrage.Fassung is { } nummer ? rezept?.FassungMitNummer(nummer) : rezept?.MassgeblicheFassung;
        if (fassung is null)
        {
            return null;
        }

        var zielgewicht = Gewicht.Erfasst(abfrage.ZielgewichtGramm);
        var zutaten = fassung.SkaliertAuf(zielgewicht).Select(ZutatMenge.Aus).ToList();
        return new SkaliertesRezept(rezept!.Id.Wert, rezept.Name.Wert, fassung.Nummer, zielgewicht.Gramm, zutaten);
    }
}
