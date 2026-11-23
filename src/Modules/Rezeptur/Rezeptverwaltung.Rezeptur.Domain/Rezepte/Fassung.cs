using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Rezepte;

/// <summary>
/// Ein Stand eines Rezepts (ADR 0013). Freigegebene Fassungen sind unveränderlich; geändert wird nur ein Entwurf,
/// und zwar ausschließlich über das Aggregat <see cref="Rezept"/>.
/// </summary>
public sealed class Fassung
{
    private readonly List<Zutatenzeile> _zutaten = [];

    private Fassung(int nummer, FassungsStatus status, DateTimeOffset erstelltAm, DateTimeOffset? freigegebenAm)
    {
        Nummer = nummer;
        Status = status;
        ErstelltAm = erstelltAm;
        FreigegebenAm = freigegebenAm;
    }

    public int Nummer { get; private set; }

    public FassungsStatus Status { get; private set; }

    public DateTimeOffset ErstelltAm { get; private set; }

    public DateTimeOffset? FreigegebenAm { get; private set; }

    public bool IstEntwurf => Status == FassungsStatus.Entwurf;

    public IReadOnlyList<Zutatenzeile> Zutaten => _zutaten.AsReadOnly();

    public Gewicht Gesamtgewicht => _zutaten.Aggregate(Gewicht.Null, (summe, zeile) => summe + zeile.Menge);

    internal static Fassung NeuerEntwurf(int nummer, IEnumerable<Zutatenzeile> zutaten, DateTimeOffset erstelltAm)
    {
        var fassung = new Fassung(nummer, FassungsStatus.Entwurf, erstelltAm, freigegebenAm: null);
        fassung.ZutatenSetzen(zutaten);
        return fassung;
    }

    /// <summary>Alle Mengen mit dem Faktor multipliziert. Die Fassung selbst bleibt unverändert.</summary>
    public IReadOnlyList<Zutatenzeile> Skaliert(Skalierungsfaktor faktor) =>
        _zutaten.Select(zeile => zeile.Skaliert(faktor)).ToList().AsReadOnly();

    /// <summary>Mengen so umgerechnet, dass die Summe aller Zutaten dem Zielgewicht entspricht.</summary>
    public IReadOnlyList<Zutatenzeile> SkaliertAuf(Gewicht zielgewicht)
    {
        // Erst die Eingabe prüfen (422), dann den Zustand (409): 409 setzt eine gültige Anfrage voraus (ADR 0009).
        DomainRuleViolationException.Wenn(zielgewicht.IstNull, "skalierung.ziel_null", "Das Zielgewicht muss größer als 0 g sein.");
        DomainRuleViolationException.ZustandWenn(_zutaten.Count == 0, "rezept.ohne_zutaten", "Ein Rezept ohne Zutaten kann nicht skaliert werden.");
        return Skaliert(Skalierungsfaktor.Zwischen(Gesamtgewicht, zielgewicht));
    }

    internal void ZutatenSetzen(IEnumerable<Zutatenzeile> zutaten)
    {
        var neu = zutaten.ToList(); // wirft ArgumentNullException bei null
        foreach (var zeile in neu)
        {
            ArgumentNullException.ThrowIfNull(zeile);
        }

        // Jede Fassung besitzt eigene Zeilen-Instanzen, auch wenn ein Entwurf eine freigegebene Fassung kopiert.
        // Die Zeilen sind unveränderlich; die Kopie verhindert aber, dass ein Objekt zu zwei Fassungen gehört.
        _zutaten.Clear();
        _zutaten.AddRange(neu.Select(zeile => zeile with { }));
    }

    internal void ZutatHinzufuegen(Zutatenzeile zeile)
    {
        ArgumentNullException.ThrowIfNull(zeile);
        _zutaten.Add(zeile);
    }

    internal void Freigeben(DateTimeOffset zeitpunkt)
    {
        Status = FassungsStatus.Freigegeben;
        FreigegebenAm = zeitpunkt;
    }
}
