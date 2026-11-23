using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Application.Rezepte;

/// <param name="Fassung">Nummer der Fassung; <c>null</c> = maßgebliche Fassung.</param>
public sealed record RezeptSkalieren(Guid RezeptId, decimal ZielgewichtGramm, int? Fassung = null);

/// <param name="Fassung">Nummer der Fassung; <c>null</c> = maßgebliche Fassung.</param>
public sealed record RezeptAufFormSkalieren(Guid RezeptId, Form Zielform, int? Fassung = null);

/// <param name="Faktor">Faktor, mit dem alle Mengen multipliziert wurden, auf <see cref="FaktorSignifikanteStellen"/> signifikante Stellen gerundet.</param>
/// <param name="Zielform">Zielform bei Umrechnung auf eine Form, sonst <c>null</c>.</param>
public sealed record SkaliertesRezept(
    Guid RezeptId,
    string Name,
    int Fassung,
    decimal Faktor,
    Form? Zielform,
    decimal ZielgewichtGramm,
    IReadOnlyList<ZutatMenge> Zutaten)
{
    /// <summary>
    /// Ausgabegenauigkeit des Faktors in signifikanten Stellen. Intern wird ungerundet gerechnet; über π entsteht
    /// sonst Rechenrauschen in der 28. Stelle (Ring 13 → 26 ergäbe 4,0000000000000000000000000004). Signifikante
    /// statt Nachkommastellen, damit auch sehr kleine Faktoren (z. B. 1e-7) nie als 0 erscheinen (Vertrag: > 0).
    /// </summary>
    public const int FaktorSignifikanteStellen = 12;

    internal static decimal FaktorFuerAusgabe(Skalierungsfaktor faktor)
    {
        var wert = faktor.Wert;
        var groessenordnung = (int)Math.Floor(Math.Log10((double)wert));
        var nachkommastellen = Math.Clamp(FaktorSignifikanteStellen - 1 - groessenordnung, 0, 28);
        var gerundet = decimal.Round(wert, nachkommastellen, MidpointRounding.AwayFromZero);

        // Nachkommanullen entfernen (4.00000000000 → 4), damit Anzeige und Sprachausgabe ruhig bleiben.
        return gerundet / 1.0000000000000000000000000000m;
    }
}

public sealed class RezeptSkalierenHandler(IRezeptRepository repository)
{
    /// <returns><c>null</c>, wenn es das Rezept oder die angegebene Fassung nicht gibt.</returns>
    public async Task<SkaliertesRezept?> HandleAsync(RezeptSkalieren abfrage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(abfrage);

        var (rezept, fassung) = await FassungLadenAsync(repository, abfrage.RezeptId, abfrage.Fassung, cancellationToken).ConfigureAwait(false);
        if (fassung is null)
        {
            return null;
        }

        var zielgewicht = Gewicht.Erfasst(abfrage.ZielgewichtGramm);
        var zutaten = fassung.SkaliertAuf(zielgewicht).Select(ZutatMenge.Aus).ToList();
        var faktor = Skalierungsfaktor.Zwischen(fassung.Gesamtgewicht, zielgewicht);
        return new SkaliertesRezept(rezept!.Id.Wert, rezept.Name.Wert, fassung.Nummer, SkaliertesRezept.FaktorFuerAusgabe(faktor), null, zielgewicht.Gramm, zutaten);
    }

    /// <summary>Lädt Rezept und Fassung; ohne Nummer die maßgebliche Fassung.</summary>
    internal static async Task<(Rezept? Rezept, Fassung? Fassung)> FassungLadenAsync(
        IRezeptRepository repository,
        Guid rezeptId,
        int? nummer,
        CancellationToken cancellationToken)
    {
        var rezept = await repository.LadenAsync(new RezeptId(rezeptId), cancellationToken).ConfigureAwait(false);
        var fassung = nummer is { } n ? rezept?.FassungMitNummer(n) : rezept?.MassgeblicheFassung;
        return (rezept, fassung);
    }
}

/// <summary>Rechnet eine Fassung auf eine andere Form um (über das Volumen, ADR 0014).</summary>
public sealed class RezeptAufFormSkalierenHandler(IRezeptRepository repository)
{
    /// <returns><c>null</c>, wenn es das Rezept oder die angegebene Fassung nicht gibt.</returns>
    public async Task<SkaliertesRezept?> HandleAsync(RezeptAufFormSkalieren abfrage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(abfrage);

        var (rezept, fassung) = await RezeptSkalierenHandler
            .FassungLadenAsync(repository, abfrage.RezeptId, abfrage.Fassung, cancellationToken)
            .ConfigureAwait(false);
        if (fassung is null)
        {
            return null;
        }

        // Eingabe zuerst (422), dann der Zustand der Fassung (409), siehe ADR 0009.
        var zielform = abfrage.Zielform.AlsBezugsform();
        var umrechnung = fassung.UmgerechnetAuf(zielform);
        var zutaten = umrechnung.Zutaten.Select(ZutatMenge.Aus).ToList();
        var gesamt = decimal.Round(
            umrechnung.Zutaten.Sum(z => z.Menge.Gramm),
            ZutatMenge.AnzeigeNachkommastellen,
            MidpointRounding.AwayFromZero);
        return new SkaliertesRezept(rezept!.Id.Wert, rezept.Name.Wert, fassung.Nummer, SkaliertesRezept.FaktorFuerAusgabe(umrechnung.Faktor), Form.Aus(zielform), gesamt, zutaten);
    }
}
