using Rezeptverwaltung.Rezeptur.Application.Rezepte;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Application.Tests;

/// <summary>Unabhängiger Test (pr-test): Zeitpunkte, Fehlerfälle und Fassungsauswahl in den Use Cases.</summary>
public sealed class FassungenGrenzfallUseCaseTests
{
    private readonly InMemoryRezeptRepositoryFake _repository = new();
    private readonly LaufendeUhr _uhr = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Jeder Aufruf liefert eine Stunde später als der vorige, beginnend bei <see cref="Testzeit.Jetzt"/>.</summary>
    private sealed class LaufendeUhr : TimeProvider
    {
        private int _aufrufe;

        public static DateTimeOffset Aufruf(int nummer) => Testzeit.Jetzt.AddHours(nummer - 1);

        public override DateTimeOffset GetUtcNow() => Aufruf(++_aufrufe);
    }

    private async Task<Guid> AnlegenAsync(params ZutatEingabe[] zutaten) =>
        (await new RezeptAnlegenHandler(_repository, _uhr).HandleAsync(new RezeptAnlegen("Wiener Masse", zutaten), _ct)).Id;

    [Fact]
    public async Task Erstellungs_und_Freigabezeitpunkt_kommen_aus_der_Uhr_und_bleiben_erhalten()
    {
        var id = await AnlegenAsync(new ZutatEingabe("Eier", 300));                          // Uhr 1
        await new EntwurfFreigebenHandler(_repository, _uhr).HandleAsync(id, _repository.Stand(id), _ct);           // Uhr 2
        await new EntwurfAnlegenHandler(_repository, _uhr).HandleAsync(id, _repository.Stand(id), _ct);             // Uhr 3
        await new EntwurfBearbeitenHandler(_repository).HandleAsync(new EntwurfBearbeiten(id, [new ZutatEingabe("Eier", 310)], _repository.Stand(id)), _ct);
        var freigegeben = await new EntwurfFreigebenHandler(_repository, _uhr).HandleAsync(id, _repository.Stand(id), _ct); // Uhr 4

        var details = (await new RezeptAbfragenHandler(_repository).HandleAsync(id, _ct))!;

        details.Fassungen.ShouldBe(
        [
            new FassungUebersicht(1, FassungsStatus.Freigegeben, LaufendeUhr.Aufruf(1), LaufendeUhr.Aufruf(2)),
            new FassungUebersicht(2, FassungsStatus.Freigegeben, LaufendeUhr.Aufruf(3), LaufendeUhr.Aufruf(4)),
        ]);
        freigegeben!.ErstelltAm.ShouldBe(LaufendeUhr.Aufruf(3));
        freigegeben.FreigegebenAm.ShouldBe(LaufendeUhr.Aufruf(4));
    }

    [Fact]
    public async Task Fassungsuebersicht_ist_nach_Nummer_sortiert_und_zeigt_Luecke_nach_Verwerfen()
    {
        var id = await AnlegenAsync(new ZutatEingabe("Eier", 300));
        await new EntwurfFreigebenHandler(_repository, _uhr).HandleAsync(id, _repository.Stand(id), _ct);
        await new EntwurfAnlegenHandler(_repository, _uhr).HandleAsync(id, _repository.Stand(id), _ct);
        await new EntwurfVerwerfenHandler(_repository).HandleAsync(id, _repository.Stand(id), _ct);

        var neu = await new EntwurfAnlegenHandler(_repository, _uhr).HandleAsync(id, _repository.Stand(id), _ct);
        var details = (await new RezeptAbfragenHandler(_repository).HandleAsync(id, _ct))!;

        neu!.Nummer.ShouldBe(3);
        details.Fassungen.Select(f => f.Nummer).ShouldBe([1, 3]);
        details.Entwurf.ShouldBe(3);
        details.AktuelleFassung.ShouldBe(1);
        details.MassgeblicheFassung.ShouldBe(1);
        (await new FassungAbfragenHandler(_repository).HandleAsync(id, 2, _ct)).ShouldBeNull();
        (await new RezeptSkalierenHandler(_repository).HandleAsync(new RezeptSkalieren(id, 100, Fassung: 2), _ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Zweiter_Entwurf_wird_abgewiesen_und_nichts_gespeichert()
    {
        var id = await AnlegenAsync(new ZutatEingabe("Eier", 300));
        var vorher = _repository.Speichervorgaenge;

        var verletzung = await Should.ThrowAsync<DomainRuleViolationException>(
            () => new EntwurfAnlegenHandler(_repository, _uhr).HandleAsync(id, _repository.Stand(id), _ct));

        verletzung.Code.ShouldBe("rezept.entwurf_vorhanden");
        verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
        _repository.Speichervorgaenge.ShouldBe(vorher);
    }

    [Fact]
    public async Task Einziger_Entwurf_wird_nicht_verworfen_und_nichts_gespeichert()
    {
        var id = await AnlegenAsync();

        var verletzung = await Should.ThrowAsync<DomainRuleViolationException>(
            () => new EntwurfVerwerfenHandler(_repository).HandleAsync(id, _repository.Stand(id), _ct));

        verletzung.Code.ShouldBe("rezept.einzige_fassung");
        _repository.Speichervorgaenge.ShouldBe(0);
        (await new RezeptAbfragenHandler(_repository).HandleAsync(id, _ct))!.Entwurf.ShouldBe(1);
    }

    [Fact]
    public async Task Leeren_Entwurf_skalieren_ist_ein_Zustandskonflikt_ungueltiges_Ziel_hat_Vorrang()
    {
        var id = await AnlegenAsync(new ZutatEingabe("Eier", 300));
        await new EntwurfFreigebenHandler(_repository, _uhr).HandleAsync(id, _repository.Stand(id), _ct);
        await new EntwurfAnlegenHandler(_repository, _uhr).HandleAsync(id, _repository.Stand(id), _ct);
        await new EntwurfBearbeitenHandler(_repository).HandleAsync(new EntwurfBearbeiten(id, [], _repository.Stand(id)), _ct);
        var handler = new RezeptSkalierenHandler(_repository);

        var zustand = await Should.ThrowAsync<DomainRuleViolationException>(() => handler.HandleAsync(new RezeptSkalieren(id, 100, Fassung: 2), _ct));
        var eingabe = await Should.ThrowAsync<DomainRuleViolationException>(() => handler.HandleAsync(new RezeptSkalieren(id, 0.001m, Fassung: 2), _ct));

        zustand.Code.ShouldBe("rezept.ohne_zutaten");
        zustand.Art.ShouldBe(Regelart.Zustandskonflikt);
        eingabe.Code.ShouldBe("gewicht.zu_genau");
        eingabe.Art.ShouldBe(Regelart.UngueltigeEingabe);
        (await handler.HandleAsync(new RezeptSkalieren(id, 600), _ct))!.Zutaten.ShouldHaveSingleItem().Gramm.ShouldBe(600m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public async Task Fassungsnummer_ausserhalb_ergibt_nicht_gefunden(int nummer)
    {
        var id = await AnlegenAsync(new ZutatEingabe("Eier", 300));

        (await new FassungAbfragenHandler(_repository).HandleAsync(id, nummer, _ct)).ShouldBeNull();
        (await new RezeptSkalierenHandler(_repository).HandleAsync(new RezeptSkalieren(id, 100, nummer), _ct)).ShouldBeNull();
    }
}
