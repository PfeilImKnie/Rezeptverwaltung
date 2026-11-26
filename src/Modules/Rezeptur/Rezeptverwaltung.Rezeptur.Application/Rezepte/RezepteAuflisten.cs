using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

/// <summary>Eintrag der Rezeptliste.</summary>
public sealed record RezeptUebersicht(Guid Id, string Name, int MassgeblicheFassung, int? AktuelleFassung, int? Entwurf)
{
    internal static RezeptUebersicht Aus(Rezept rezept) => new(
        rezept.Id.Wert,
        rezept.Name.Wert,
        rezept.MassgeblicheFassung.Nummer,
        rezept.AktuelleFassung?.Nummer,
        rezept.Entwurf?.Nummer);
}

public sealed class RezepteAuflistenHandler(IRezeptRepository repository, IAktuellerBenutzer benutzer)
{
    public async Task<IReadOnlyList<RezeptUebersicht>> HandleAsync(string? suchtext, CancellationToken cancellationToken)
    {
        var betrieb = ZugriffVerweigertException.Pruefen(benutzer, Rechte.Lesen, "Rezepte auflisten");
        var suche = Rezeptsuche.Von(suchtext);

        var rezepte = await repository.AlleAsync(cancellationToken).ConfigureAwait(false);
        return rezepte
            .Where(r => Rechte.NurEigenes(r, betrieb) is not null && suche.Passt(r.Name))
            .OrderBy(r => r.Name, Rezeptsuche.Reihenfolge)
            .Select(RezeptUebersicht.Aus)
            .ToList();
    }
}
