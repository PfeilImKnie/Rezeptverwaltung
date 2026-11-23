using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Rezepte;

/// <summary>
/// Aggregat „Rezept“. Erster Schnitt: Name und Zutatenliste nach Gewicht, skalierbar.
/// Folgt: Komponenten (Grundrezepte), Arbeitsschritte, Versionen, Ausbeute.
/// </summary>
public sealed class Rezept : AggregateRoot<RezeptId>
{
    private readonly List<Zutatenzeile> _zutaten = [];

    private Rezept(RezeptId id, RezeptName name) : base(id) => Name = name;

    public RezeptName Name { get; private set; }

    public IReadOnlyList<Zutatenzeile> Zutaten => _zutaten.AsReadOnly();

    public Gewicht Gesamtgewicht => _zutaten.Aggregate(Gewicht.Null, (summe, zeile) => summe + zeile.Menge);

    public static Rezept Anlegen(RezeptName name, IEnumerable<Zutatenzeile> zutaten)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(zutaten);

        var rezept = new Rezept(RezeptId.Neu(), name);
        foreach (var zeile in zutaten)
        {
            rezept.ZutatHinzufuegen(zeile);
        }

        rezept.Melde(new RezeptAngelegt(rezept.Id));
        return rezept;
    }

    public void ZutatHinzufuegen(Zutatenzeile zeile)
    {
        ArgumentNullException.ThrowIfNull(zeile);
        _zutaten.Add(zeile);
    }

    /// <summary>Alle Mengen mit dem Faktor multipliziert. Das Rezept selbst bleibt unverändert.</summary>
    public IReadOnlyList<Zutatenzeile> Skaliert(Skalierungsfaktor faktor) =>
        _zutaten.Select(zeile => zeile.Skaliert(faktor)).ToList().AsReadOnly();

    /// <summary>Mengen so umgerechnet, dass die Summe aller Zutaten dem Zielgewicht entspricht.</summary>
    public IReadOnlyList<Zutatenzeile> SkaliertAuf(Gewicht zielgewicht)
    {
        DomainRuleViolationException.Wenn(_zutaten.Count == 0, "rezept.ohne_zutaten", "Ein Rezept ohne Zutaten kann nicht skaliert werden.");
        DomainRuleViolationException.Wenn(zielgewicht.IstNull, "skalierung.ziel_null", "Das Zielgewicht muss größer als 0 g sein.");
        return Skaliert(Skalierungsfaktor.Zwischen(Gesamtgewicht, zielgewicht));
    }
}

public sealed record RezeptAngelegt(RezeptId RezeptId) : IDomainEvent;
