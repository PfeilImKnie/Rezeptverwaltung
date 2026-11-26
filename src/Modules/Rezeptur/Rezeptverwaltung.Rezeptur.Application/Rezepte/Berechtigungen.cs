using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

/// <summary>Was die angemeldete Person im Modul Rezeptur darf; für Oberflächen, die Aktionen ohne Recht ausblenden.</summary>
public sealed record Berechtigungen(bool Lesen, bool Bearbeiten, bool Freigeben);

public sealed class BerechtigungenAbfragenHandler(IAktuellerBenutzer benutzer)
{
    /// <summary>
    /// Maßgeblich bleibt die Prüfung jeder einzelnen Operation. Ohne passende Rolle ist alles <c>false</c>;
    /// Rollen in mehreren Betrieben ergeben wie überall 403 <c>betrieb.nicht_eindeutig</c>.
    /// </summary>
    public Berechtigungen Handle()
    {
        if (benutzer.BetriebMehrdeutig)
        {
            ZugriffVerweigertException.Pruefen(benutzer, Rechte.Lesen, "Rechte abfragen"); // wirft betrieb.nicht_eindeutig
        }

        return new Berechtigungen(Hat(Rechte.Lesen), Hat(Rechte.Bearbeiten), Hat(Rechte.Freigeben));
    }

    private bool Hat(IReadOnlySet<Rolle> berechtigteRollen) =>
        benutzer.Betrieb is not null && benutzer.Rollen.Overlaps(berechtigteRollen);
}
