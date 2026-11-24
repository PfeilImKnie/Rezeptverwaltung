using Rezeptverwaltung.Rezeptur.Application.Rezepte;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Application.Tests;

public sealed class EntwurfUseCaseTests
{
    private readonly InMemoryRezeptRepositoryFake _repository = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<Guid> FreigegebenesRezeptAsync()
    {
        var details = await new RezeptAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA)
            .HandleAsync(new RezeptAnlegen("Wiener Masse", [new ZutatEingabe("Eier", 300), new ZutatEingabe("Zucker", 150)]), _ct);
        await new EntwurfFreigebenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(details.Id, _repository.Stand(details.Id), _ct);
        return details.Id;
    }

    [Fact]
    public async Task Anlegen_liefert_Fassungsuebersicht_mit_Entwurf_1()
    {
        var details = await new RezeptAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA)
            .HandleAsync(new RezeptAnlegen("Neu", [new ZutatEingabe("Mehl", 100)]), _ct);

        details.MassgeblicheFassung.ShouldBe(1);
        details.AktuelleFassung.ShouldBeNull();
        details.Entwurf.ShouldBe(1);
        details.Fassungen.ShouldBe([new FassungUebersicht(1, FassungsStatus.Entwurf, Testzeit.Jetzt, null)]);
    }

    [Fact]
    public async Task Kompletter_Ablauf_Entwurf_bearbeiten_und_freigeben()
    {
        var id = await FreigegebenesRezeptAsync();

        var entwurf = await new EntwurfAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(id, _repository.Stand(id), _ct);
        var bearbeitet = await new EntwurfBearbeitenHandler(_repository, Testbenutzer.InhaberA)
            .HandleAsync(new EntwurfBearbeiten(id, [new ZutatEingabe("Eier", 320), new ZutatEingabe("Zucker", 140)], _repository.Stand(id)), _ct);
        var freigegeben = await new EntwurfFreigebenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(id, _repository.Stand(id), _ct);
        var details = await new RezeptAbfragenHandler(_repository, Testbenutzer.InhaberA).HandleAsync(id, _ct);

        entwurf!.Nummer.ShouldBe(2);
        bearbeitet!.GesamtgewichtGramm.ShouldBe(460m);
        freigegeben!.Status.ShouldBe(FassungsStatus.Freigegeben);
        freigegeben.FreigegebenAm.ShouldBe(Testzeit.Jetzt);
        details!.MassgeblicheFassung.ShouldBe(2);
        details.AktuelleFassung.ShouldBe(2);
        details.Entwurf.ShouldBeNull();
        details.Fassungen.Select(f => f.Nummer).ShouldBe([1, 2]);
        _repository.Speichervorgaenge.ShouldBe(4);
    }

    [Fact]
    public async Task Fassung_1_bleibt_nach_neuer_Freigabe_abrufbar_und_unveraendert()
    {
        var id = await FreigegebenesRezeptAsync();
        await new EntwurfAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(id, _repository.Stand(id), _ct);
        await new EntwurfBearbeitenHandler(_repository, Testbenutzer.InhaberA).HandleAsync(new EntwurfBearbeiten(id, [new ZutatEingabe("Eier", 1)], _repository.Stand(id)), _ct);
        await new EntwurfFreigebenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(id, _repository.Stand(id), _ct);

        var fassung1 = await new FassungAbfragenHandler(_repository, Testbenutzer.InhaberA).HandleAsync(id, 1, _ct);

        fassung1!.Zutaten.ShouldBe([new ZutatMenge("Eier", 300), new ZutatMenge("Zucker", 150)]);
    }

    [Fact]
    public async Task Verwerfen_entfernt_den_Entwurf()
    {
        var id = await FreigegebenesRezeptAsync();
        await new EntwurfAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(id, _repository.Stand(id), _ct);

        (await new EntwurfVerwerfenHandler(_repository, Testbenutzer.InhaberA).HandleAsync(id, _repository.Stand(id), _ct)).ShouldNotBeNull();

        (await new RezeptAbfragenHandler(_repository, Testbenutzer.InhaberA).HandleAsync(id, _ct))!.Entwurf.ShouldBeNull();
    }

    [Fact]
    public async Task Skalieren_einer_bestimmten_Fassung()
    {
        var id = await FreigegebenesRezeptAsync();
        await new EntwurfAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(id, _repository.Stand(id), _ct);
        await new EntwurfBearbeitenHandler(_repository, Testbenutzer.InhaberA)
            .HandleAsync(new EntwurfBearbeiten(id, [new ZutatEingabe("Eier", 200), new ZutatEingabe("Zucker", 200)], _repository.Stand(id)), _ct);
        var handler = new RezeptSkalierenHandler(_repository, Testbenutzer.InhaberA);

        var massgeblich = await handler.HandleAsync(new RezeptSkalieren(id, 900), _ct);
        var entwurf = await handler.HandleAsync(new RezeptSkalieren(id, 900, Fassung: 2), _ct);

        massgeblich!.Fassung.ShouldBe(1);
        massgeblich.Zutaten.Select(z => z.Gramm).ShouldBe([600m, 300m]);
        entwurf!.Fassung.ShouldBe(2);
        entwurf.Zutaten.Select(z => z.Gramm).ShouldBe([450m, 450m]);
    }

    [Fact]
    public async Task Unbekanntes_Rezept_oder_unbekannte_Fassung_ergibt_nicht_gefunden()
    {
        var id = await FreigegebenesRezeptAsync();
        var unbekannt = Guid.NewGuid();

        (await new FassungAbfragenHandler(_repository, Testbenutzer.InhaberA).HandleAsync(id, 7, _ct)).ShouldBeNull();
        (await new FassungAbfragenHandler(_repository, Testbenutzer.InhaberA).HandleAsync(unbekannt, 1, _ct)).ShouldBeNull();
        (await new RezeptSkalierenHandler(_repository, Testbenutzer.InhaberA).HandleAsync(new RezeptSkalieren(id, 100, Fassung: 7), _ct)).ShouldBeNull();
        (await new EntwurfAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(unbekannt, _repository.Stand(unbekannt), _ct)).ShouldBeNull();
        (await new EntwurfBearbeitenHandler(_repository, Testbenutzer.InhaberA).HandleAsync(new EntwurfBearbeiten(unbekannt, [], _repository.Stand(unbekannt)), _ct)).ShouldBeNull();
        (await new EntwurfFreigebenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(unbekannt, _repository.Stand(unbekannt), _ct)).ShouldBeNull();
        (await new EntwurfVerwerfenHandler(_repository, Testbenutzer.InhaberA).HandleAsync(unbekannt, _repository.Stand(unbekannt), _ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Ungueltige_Eingabe_wird_vor_fehlendem_Entwurf_gemeldet()
    {
        var id = await FreigegebenesRezeptAsync();

        var verletzung = await Should.ThrowAsync<DomainRuleViolationException>(
            () => new EntwurfBearbeitenHandler(_repository, Testbenutzer.InhaberA).HandleAsync(new EntwurfBearbeiten(id, [new ZutatEingabe("Eier", 0.001m)], _repository.Stand(id)), _ct));

        verletzung.Code.ShouldBe("gewicht.zu_genau");
    }

    [Fact]
    public async Task Verletzte_Regel_speichert_nichts()
    {
        var id = await FreigegebenesRezeptAsync();
        var vorher = _repository.Speichervorgaenge;

        await Should.ThrowAsync<DomainRuleViolationException>(() => new EntwurfFreigebenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(id, _repository.Stand(id), _ct));

        _repository.Speichervorgaenge.ShouldBe(vorher);
    }
}
