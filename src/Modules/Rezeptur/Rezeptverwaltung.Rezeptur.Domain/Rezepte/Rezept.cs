using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Rezepte;

/// <summary>
/// Aggregat „Rezept“: Identität und Name, dazu die Fassungen 1…n (ADR 0013).
/// Regeln: Freigegebene Fassungen sind unveränderlich; es gibt höchstens einen Entwurf;
/// Änderungen am Rezept wirken nie stillschweigend auf freigegebene Fassungen.
/// Folgt: Bezugsform, Komponenten (Grundrezepte), Arbeitsschritte.
/// </summary>
public sealed class Rezept : AggregateRoot<RezeptId>
{
    private readonly List<Fassung> _fassungen = [];

    private Rezept(RezeptId id, BetriebId betriebId, RezeptName name)
        : base(id)
    {
        BetriebId = betriebId;
        Name = name;
    }

    /// <summary>Betrieb, dem das Rezept gehört (ADR 0015). Wird beim Anlegen festgelegt und ändert sich nie.</summary>
    public BetriebId BetriebId { get; private set; }

    public RezeptName Name { get; private set; }

    public IReadOnlyList<Fassung> Fassungen => _fassungen.AsReadOnly();

    /// <summary>
    /// Zuletzt vergebene Fassungsnummer. Nummern werden nie wiederverwendet, auch nicht nach dem Verwerfen eines
    /// Entwurfs, damit eine Zubereitung mit „Fassung n“ immer eindeutig bleibt.
    /// </summary>
    public int LetzteFassungsnummer { get; private set; }

    /// <summary>Der Entwurf, falls es einen gibt.</summary>
    public Fassung? Entwurf => _fassungen.SingleOrDefault(f => f.IstEntwurf);

    /// <summary>Die neueste freigegebene Fassung, falls es eine gibt.</summary>
    public Fassung? AktuelleFassung => _fassungen.Where(f => !f.IstEntwurf).MaxBy(f => f.Nummer);

    /// <summary>
    /// Die Fassung, auf die sich Abfragen ohne Fassungsangabe beziehen: die aktuelle freigegebene,
    /// solange es keine gibt, der Entwurf. Ein Rezept hat immer mindestens eine Fassung.
    /// </summary>
    public Fassung MassgeblicheFassung => AktuelleFassung ?? Entwurf!;

    /// <summary>Zutaten der maßgeblichen Fassung.</summary>
    public IReadOnlyList<Zutatenzeile> Zutaten => MassgeblicheFassung.Zutaten;

    /// <summary>Gesamtgewicht der maßgeblichen Fassung.</summary>
    public Gewicht Gesamtgewicht => MassgeblicheFassung.Gesamtgewicht;

    /// <summary>Legt ein neues Rezept an. Fassung 1 beginnt als Entwurf.</summary>
    public static Rezept Anlegen(
        BetriebId betrieb,
        RezeptName name,
        IEnumerable<Zutatenzeile> zutaten,
        DateTimeOffset zeitpunkt,
        Bezugsform? bezugsform = null)
    {
        ArgumentNullException.ThrowIfNull(name); // zutaten prüft Fassung.NeuerEntwurf

        var rezept = new Rezept(RezeptId.Neu(), betrieb, name);
        rezept.LetzteFassungsnummer = 1;
        rezept._fassungen.Add(Fassung.NeuerEntwurf(rezept.LetzteFassungsnummer, zutaten, bezugsform, zeitpunkt));
        rezept.Melde(new RezeptAngelegt(rezept.Id));
        return rezept;
    }

    public Fassung? FassungMitNummer(int nummer) => _fassungen.SingleOrDefault(f => f.Nummer == nummer);

    /// <summary>Neuer Entwurf als Kopie der aktuellen freigegebenen Fassung.</summary>
    public Fassung EntwurfAnlegen(DateTimeOffset zeitpunkt)
    {
        DomainRuleViolationException.ZustandWenn(
            Entwurf is not null,
            "rezept.entwurf_vorhanden",
            "Es gibt bereits einen Entwurf. Erst freigeben oder verwerfen.");

        var vorlage = AktuelleFassung!;

        // Fassungen aus der Zeit vor der Obergrenze (Issue #6) können mehr Zeilen haben. Der Zustand der Vorlage
        // verhindert den Entwurf (409), nicht eine Eingabe der Person (422).
        DomainRuleViolationException.ZustandWenn(
            vorlage.Zutaten.Count > Fassung.MaximaleZutatenzeilen,
            "fassung.zu_viele_zutaten",
            $"Die aktuelle Fassung hat mehr als {Fassung.MaximaleZutatenzeilen} Zutatenzeilen und kann nicht als Entwurf übernommen werden.");
        LetzteFassungsnummer++;
        var entwurf = Fassung.NeuerEntwurf(LetzteFassungsnummer, vorlage.Zutaten, vorlage.Bezugsform, zeitpunkt);
        _fassungen.Add(entwurf);
        return entwurf;
    }

    /// <summary>
    /// Ersetzt den bearbeitbaren Inhalt des Entwurfs vollständig: Zutaten und Bezugsform (<c>null</c> = ohne Form).
    /// </summary>
    public Fassung EntwurfBearbeiten(IEnumerable<Zutatenzeile> zutaten, Bezugsform? bezugsform = null)
    {
        var entwurf = VorhandenerEntwurf();
        entwurf.InhaltSetzen(zutaten, bezugsform);
        return entwurf;
    }

    public void ZutatHinzufuegen(Zutatenzeile zeile) => VorhandenerEntwurf().ZutatHinzufuegen(zeile);

    /// <summary>Der Entwurf wird unveränderlich und zur aktuellen Fassung.</summary>
    public Fassung EntwurfFreigeben(DateTimeOffset zeitpunkt)
    {
        var entwurf = VorhandenerEntwurf();
        DomainRuleViolationException.ZustandWenn(
            entwurf.Zutaten.Count == 0,
            "fassung.ohne_zutaten",
            "Eine Fassung ohne Zutaten kann nicht freigegeben werden.");

        entwurf.Freigeben(zeitpunkt);
        Melde(new FassungFreigegeben(Id, entwurf.Nummer));
        return entwurf;
    }

    /// <summary>Verwirft den Entwurf. Ein Rezept muss mindestens eine Fassung behalten.</summary>
    public void EntwurfVerwerfen()
    {
        var entwurf = VorhandenerEntwurf();
        DomainRuleViolationException.ZustandWenn(
            _fassungen.Count == 1,
            "rezept.einzige_fassung",
            "Der Entwurf ist die einzige Fassung und kann nicht verworfen werden.");
        _fassungen.Remove(entwurf);
    }

    /// <summary>Skaliert die maßgebliche Fassung. Das Rezept bleibt unverändert.</summary>
    public IReadOnlyList<Zutatenzeile> SkaliertAuf(Gewicht zielgewicht) => MassgeblicheFassung.SkaliertAuf(zielgewicht);

    /// <summary>Skaliert die maßgebliche Fassung mit einem Faktor. Das Rezept bleibt unverändert.</summary>
    public IReadOnlyList<Zutatenzeile> Skaliert(Skalierungsfaktor faktor) => MassgeblicheFassung.Skaliert(faktor);

    private Fassung VorhandenerEntwurf()
    {
        var entwurf = Entwurf;
        DomainRuleViolationException.ZustandWenn(entwurf is null, "rezept.kein_entwurf", "Es gibt keinen Entwurf.");
        return entwurf!;
    }
}

public sealed record RezeptAngelegt(RezeptId RezeptId) : IDomainEvent;

public sealed record FassungFreigegeben(RezeptId RezeptId, int Nummer) : IDomainEvent;
