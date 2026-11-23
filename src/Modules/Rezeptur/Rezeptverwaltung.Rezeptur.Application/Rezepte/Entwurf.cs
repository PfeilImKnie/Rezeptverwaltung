using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;
using static Rezeptverwaltung.Rezeptur.Application.Rezepte.Codes;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

// Arbeitsablauf mit Entwurf und Freigabe (ADR 0013). Jeder Handler: laden, Stand prüfen, Aggregat ändern, speichern.
// Reihenfolge der Prüfungen: Recht fehlt (403) · Rezept fehlt oder gehört einem anderen Betrieb (null → 404) ·
// Stand fehlt/veraltet (428/412) · Eingabe ungültig (422) · Zustand lässt es nicht zu (409).

internal static class Codes
{
    /// <summary>Stand des Rezepts beim Client ist veraltet (412).</summary>
    public const string StandVeraltet = "rezept.veraltet";
}

public sealed class EntwurfAnlegenHandler(IRezeptRepository repository, TimeProvider zeit, IAktuellerBenutzer benutzer)
{
    public async Task<FassungDetails?> HandleAsync(Guid rezeptId, string? erwarteterStand, CancellationToken cancellationToken)
    {
        var betrieb = ZugriffVerweigertException.Pruefen(benutzer, Rechte.Bearbeiten, "Entwurf anlegen");
        var rezept = Rechte.NurEigenes(await repository.LadenAsync(new RezeptId(rezeptId), cancellationToken).ConfigureAwait(false), betrieb);
        if (rezept is null)
        {
            return null;
        }

        VorbedingungException.Pruefen(repository.StandVon(rezept), erwarteterStand, StandVeraltet);
        var entwurf = rezept.EntwurfAnlegen(zeit.GetUtcNow());
        await repository.SpeichernAsync(rezept, cancellationToken).ConfigureAwait(false);
        return FassungDetails.Aus(rezept, entwurf, repository.StandVon(rezept));
    }
}

public sealed record EntwurfBearbeiten(Guid RezeptId, IReadOnlyList<ZutatEingabe> Zutaten, string? ErwarteterStand, Form? Bezugsform = null);

public sealed class EntwurfBearbeitenHandler(IRezeptRepository repository, IAktuellerBenutzer benutzer)
{
    public async Task<FassungDetails?> HandleAsync(EntwurfBearbeiten befehl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(befehl);

        var betrieb = ZugriffVerweigertException.Pruefen(benutzer, Rechte.Bearbeiten, "Entwurf bearbeiten");
        var rezept = Rechte.NurEigenes(await repository.LadenAsync(new RezeptId(befehl.RezeptId), cancellationToken).ConfigureAwait(false), betrieb);
        if (rezept is null)
        {
            return null;
        }

        VorbedingungException.Pruefen(repository.StandVon(rezept), befehl.ErwarteterStand, StandVeraltet);
        var zutaten = ZutatEingabe.AlsZutatenzeilen(befehl.Zutaten);
        var bezugsform = befehl.Bezugsform?.AlsBezugsform();
        var entwurf = rezept.EntwurfBearbeiten(zutaten, bezugsform);
        await repository.SpeichernAsync(rezept, cancellationToken).ConfigureAwait(false);
        return FassungDetails.Aus(rezept, entwurf, repository.StandVon(rezept));
    }
}

public sealed class EntwurfFreigebenHandler(IRezeptRepository repository, TimeProvider zeit, IAktuellerBenutzer benutzer)
{
    public async Task<FassungDetails?> HandleAsync(Guid rezeptId, string? erwarteterStand, CancellationToken cancellationToken)
    {
        var betrieb = ZugriffVerweigertException.Pruefen(benutzer, Rechte.Freigeben, "Entwurf freigeben");
        var rezept = Rechte.NurEigenes(await repository.LadenAsync(new RezeptId(rezeptId), cancellationToken).ConfigureAwait(false), betrieb);
        if (rezept is null)
        {
            return null;
        }

        VorbedingungException.Pruefen(repository.StandVon(rezept), erwarteterStand, StandVeraltet);
        var fassung = rezept.EntwurfFreigeben(zeit.GetUtcNow());
        await repository.SpeichernAsync(rezept, cancellationToken).ConfigureAwait(false);
        return FassungDetails.Aus(rezept, fassung, repository.StandVon(rezept));
    }
}

public sealed class EntwurfVerwerfenHandler(IRezeptRepository repository, IAktuellerBenutzer benutzer)
{
    /// <returns>Der neue Stand des Rezepts; <c>null</c>, wenn es das Rezept nicht gibt.</returns>
    public async Task<string?> HandleAsync(Guid rezeptId, string? erwarteterStand, CancellationToken cancellationToken)
    {
        var betrieb = ZugriffVerweigertException.Pruefen(benutzer, Rechte.Bearbeiten, "Entwurf verwerfen");
        var rezept = Rechte.NurEigenes(await repository.LadenAsync(new RezeptId(rezeptId), cancellationToken).ConfigureAwait(false), betrieb);
        if (rezept is null)
        {
            return null;
        }

        VorbedingungException.Pruefen(repository.StandVon(rezept), erwarteterStand, StandVeraltet);
        rezept.EntwurfVerwerfen();
        await repository.SpeichernAsync(rezept, cancellationToken).ConfigureAwait(false);
        return repository.StandVon(rezept);
    }
}
