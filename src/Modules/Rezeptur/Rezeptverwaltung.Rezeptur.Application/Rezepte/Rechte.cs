using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

/// <summary>Welche Rolle im Modul Rezeptur was darf (ADR 0015, fachlich festgelegt am 2026-11-23).</summary>
internal static class Rechte
{
    /// <summary>Rezepte und Fassungen lesen, umrechnen: alle Rollen.</summary>
    public static readonly IReadOnlySet<Rolle> Lesen = new HashSet<Rolle> { Rolle.Mitarbeiter, Rolle.Konditor, Rolle.Inhaber };

    /// <summary>Rezepte anlegen, Entwürfe anlegen, bearbeiten und verwerfen.</summary>
    public static readonly IReadOnlySet<Rolle> Bearbeiten = new HashSet<Rolle> { Rolle.Konditor, Rolle.Inhaber };

    /// <summary>Entwürfe freigeben.</summary>
    public static readonly IReadOnlySet<Rolle> Freigeben = new HashSet<Rolle> { Rolle.Inhaber };

    /// <summary>
    /// Zweite Verteidigungslinie neben dem Datenbankfilter: Ein Rezept eines anderen Betriebs verhält sich wie
    /// nicht vorhanden (404), damit nicht einmal seine Existenz erkennbar ist.
    /// </summary>
    public static Domain.Rezepte.Rezept? NurEigenes(Domain.Rezepte.Rezept? rezept, BetriebId betrieb) =>
        rezept is not null && rezept.BetriebId == betrieb ? rezept : null;
}
