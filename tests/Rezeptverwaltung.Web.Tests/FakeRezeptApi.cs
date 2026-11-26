using Rezeptverwaltung.Web.Client.Api;

namespace Rezeptverwaltung.Web.Tests;

/// <summary>
/// API-Attrappe für Seitentests: hält Rezepte im Speicher und zählt den Stand wie die echte API hoch.
/// Ein gesetzter <see cref="NaechsterFehler"/> wird beim nächsten Aufruf geworfen.
/// </summary>
internal sealed class FakeRezeptApi : IRezeptApi
{
    private readonly Dictionary<Guid, (RezeptDetails Rezept, Dictionary<int, FassungDetails> Fassungen, int Stand)> _rezepte = [];

    public List<string> Aufrufe { get; } = [];

    public ApiFehlerException? NaechsterFehler { get; set; }

    public Umrechnung? LetzteUmrechnung { get; private set; }

    public int? LetzteUmrechnungFassung { get; private set; }

    public RezeptInhalt? LetzterInhalt { get; private set; }

    public string? LetzterStand { get; private set; }

    public static readonly DateTimeOffset Zeitpunkt = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

    public Guid Hinzufuegen(string name, RezeptInhalt inhalt, bool freigegeben = false, bool mitEntwurf = false)
    {
        var id = Guid.CreateVersion7();
        var fassungen = new Dictionary<int, FassungDetails>
        {
            [1] = Fassung(id, 1, freigegeben ? "freigegeben" : "entwurf", inhalt),
        };
        if (freigegeben && mitEntwurf)
        {
            fassungen[2] = Fassung(id, 2, "entwurf", inhalt);
        }

        _rezepte[id] = (Details(id, name, fassungen), fassungen, 1);
        return id;
    }

    public string Stand(Guid id) => $"\"{_rezepte[id].Stand}\"";

    public Task<IReadOnlyList<RezeptUebersicht>> AuflistenAsync(string? suche, CancellationToken cancellationToken = default)
    {
        Protokoll($"Auflisten:{suche}");
        IReadOnlyList<RezeptUebersicht> liste = _rezepte.Values
            .Select(r => r.Rezept)
            .Where(r => string.IsNullOrWhiteSpace(suche) || r.Name.Contains(suche.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => new RezeptUebersicht(r.Id, r.Name, r.MassgeblicheFassung, r.AktuelleFassung, r.Entwurf))
            .ToList();
        return Task.FromResult(liste);
    }

    public Berechtigungen Rechte { get; set; } = new(true, true, true);

    public Task<Berechtigungen> BerechtigungenAsync(CancellationToken cancellationToken = default)
    {
        Protokoll("Berechtigungen");
        return Task.FromResult(Rechte);
    }

    public Task<MitStand<RezeptDetails>?> RezeptAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Protokoll($"Rezept:{id}");
        return Task.FromResult(_rezepte.TryGetValue(id, out var r) ? new MitStand<RezeptDetails>(r.Rezept, Stand(id)) : null);
    }

    public Task<MitStand<FassungDetails>?> FassungAsync(Guid id, int nummer, CancellationToken cancellationToken = default)
    {
        Protokoll($"Fassung:{nummer}");
        return Task.FromResult(_rezepte.TryGetValue(id, out var r) && r.Fassungen.TryGetValue(nummer, out var f)
            ? new MitStand<FassungDetails>(f, Stand(id))
            : null);
    }

    public Task<MitStand<RezeptDetails>> AnlegenAsync(string name, RezeptInhalt inhalt, CancellationToken cancellationToken = default)
    {
        Protokoll($"Anlegen:{name}");
        LetzterInhalt = inhalt;
        var id = Hinzufuegen(name, inhalt);
        return Task.FromResult(new MitStand<RezeptDetails>(_rezepte[id].Rezept, Stand(id)));
    }

    public Task<SkaliertesRezept> UmrechnenAsync(Guid id, Umrechnung ziel, int? fassung, CancellationToken cancellationToken = default)
    {
        Protokoll("Umrechnen");
        LetzteUmrechnung = ziel;
        LetzteUmrechnungFassung = fassung;
        var rezept = _rezepte[id].Rezept;
        var faktor = ziel is Umrechnung.AufGewicht g ? g.Gramm / rezept.GesamtgewichtGramm : 0.479289940828m;
        var zutaten = rezept.Zutaten.Select(z => z with { Gramm = decimal.Round(z.Gramm * faktor, 2) }).ToList();
        return Task.FromResult(new SkaliertesRezept(id, rezept.Name, fassung ?? rezept.MassgeblicheFassung, faktor,
            (ziel as Umrechnung.AufForm)?.Form, zutaten.Sum(z => z.Gramm), zutaten));
    }

    public Task<MitStand<FassungDetails>> EntwurfAnlegenAsync(Guid id, string stand, CancellationToken cancellationToken = default)
    {
        Protokoll("EntwurfAnlegen");
        LetzterStand = stand;
        var (rezept, fassungen, _) = _rezepte[id];
        var nummer = fassungen.Keys.Max() + 1;
        var vorlage = fassungen[rezept.MassgeblicheFassung];
        fassungen[nummer] = vorlage with { Nummer = nummer, Status = "entwurf", FreigegebenAm = null };
        return Task.FromResult(new MitStand<FassungDetails>(fassungen[nummer], Geaendert(id)));
    }

    public Task<MitStand<FassungDetails>> EntwurfSpeichernAsync(Guid id, string stand, RezeptInhalt inhalt, CancellationToken cancellationToken = default)
    {
        Protokoll("EntwurfSpeichern");
        LetzterStand = stand;
        LetzterInhalt = inhalt;
        var (rezept, fassungen, _) = _rezepte[id];
        var nummer = rezept.Entwurf!.Value;
        fassungen[nummer] = Fassung(id, nummer, "entwurf", inhalt);
        return Task.FromResult(new MitStand<FassungDetails>(fassungen[nummer], Geaendert(id)));
    }

    public Task EntwurfVerwerfenAsync(Guid id, string stand, CancellationToken cancellationToken = default)
    {
        Protokoll("Verwerfen");
        LetzterStand = stand;
        var (rezept, fassungen, _) = _rezepte[id];
        fassungen.Remove(rezept.Entwurf!.Value);
        Geaendert(id);
        return Task.CompletedTask;
    }

    public Task<MitStand<FassungDetails>> FreigebenAsync(Guid id, string stand, CancellationToken cancellationToken = default)
    {
        Protokoll("Freigeben");
        LetzterStand = stand;
        var (rezept, fassungen, _) = _rezepte[id];
        var nummer = rezept.Entwurf!.Value;
        fassungen[nummer] = fassungen[nummer] with { Status = "freigegeben", FreigegebenAm = Zeitpunkt };
        return Task.FromResult(new MitStand<FassungDetails>(fassungen[nummer], Geaendert(id)));
    }

    private void Protokoll(string aufruf)
    {
        Aufrufe.Add(aufruf);
        if (NaechsterFehler is { } fehler)
        {
            NaechsterFehler = null;
            throw fehler;
        }
    }

    private string Geaendert(Guid id)
    {
        var (_, fassungen, stand) = _rezepte[id];
        _rezepte[id] = (Details(id, _rezepte[id].Rezept.Name, fassungen), fassungen, stand + 1);
        return Stand(id);
    }

    private static FassungDetails Fassung(Guid id, int nummer, string status, RezeptInhalt inhalt) => new(
        id, nummer, status, Zeitpunkt, status == "freigegeben" ? Zeitpunkt : null, inhalt.Bezugsform, inhalt.Zutaten, inhalt.Zutaten.Sum(z => z.Gramm));

    private static RezeptDetails Details(Guid id, string name, Dictionary<int, FassungDetails> fassungen)
    {
        var aktuell = fassungen.Values.Where(f => !f.IstEntwurf).MaxBy(f => f.Nummer);
        var entwurf = fassungen.Values.SingleOrDefault(f => f.IstEntwurf);
        var massgeblich = aktuell ?? entwurf!;
        return new RezeptDetails(id, name, massgeblich.Zutaten, massgeblich.GesamtgewichtGramm, massgeblich.Bezugsform, massgeblich.Nummer,
            aktuell?.Nummer, entwurf?.Nummer,
            fassungen.Values.OrderBy(f => f.Nummer).Select(f => new FassungUebersicht(f.Nummer, f.Status, f.ErstelltAm, f.FreigegebenAm)).ToList());
    }
}
