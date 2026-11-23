using Rezeptverwaltung.Rezeptur.Application.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Application.Tests;

/// <summary>
/// Prüfreihenfolge 403 → 404 → 428/412 → 422 → 409 (ADR 0015) und „ohne Recht wird nichts gespeichert“,
/// abgeleitet aus den Akzeptanzkriterien (2) und (3).
/// </summary>
public sealed class PruefreihenfolgeRechteUseCaseTests
{
    private readonly InMemoryRezeptRepositoryFake _repository = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static List<ZutatEingabe> Zeilen(int anzahl) =>
        Enumerable.Range(1, anzahl).Select(i => new ZutatEingabe($"Zutat {i}", 1)).ToList();

    private async Task<Guid> AnlegenAsync(IAktuellerBenutzer benutzer) =>
        (await new RezeptAnlegenHandler(_repository, Testzeit.Anbieter, benutzer)
            .HandleAsync(new RezeptAnlegen("Sachertorte", [new ZutatEingabe("Kuvertüre", 150)]), _ct)).Id;

    [Fact]
    public async Task Fehlendes_Recht_kommt_vor_ungueltiger_Eingabe_beim_Anlegen()
    {
        var verweigert = await Should.ThrowAsync<ZugriffVerweigertException>(
            () => new RezeptAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.MitarbeiterA)
                .HandleAsync(new RezeptAnlegen("Zu viel", Zeilen(101)), _ct));

        verweigert.Code.ShouldBe("recht.fehlt");
        _repository.Gespeichert.ShouldBeEmpty();
    }

    [Fact]
    public async Task Mehrdeutiger_Betrieb_kommt_vor_ungueltiger_Eingabe_und_legt_nichts_an()
    {
        var verweigert = await Should.ThrowAsync<ZugriffVerweigertException>(
            () => new RezeptAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.ZweiBetriebe)
                .HandleAsync(new RezeptAnlegen(" ", []), _ct));

        verweigert.Code.ShouldBe("betrieb.nicht_eindeutig");
        _repository.Gespeichert.ShouldBeEmpty();
    }

    [Fact]
    public async Task Fremdes_Rezept_ergibt_nicht_vorhanden_auch_bei_veraltetem_Stand_und_ungueltiger_Eingabe()
    {
        // 404 vor 412 und 422: Sonst verriete die Fehlermeldung, dass es das Rezept gibt.
        var id = await AnlegenAsync(Testbenutzer.InhaberA);

        var ergebnis = await new EntwurfBearbeitenHandler(_repository, Testbenutzer.InhaberB)
            .HandleAsync(new EntwurfBearbeiten(id, Zeilen(101), "\"veraltet\""), _ct);

        ergebnis.ShouldBeNull();
        _repository.Speichervorgaenge.ShouldBe(0);
    }

    [Fact]
    public async Task Fremdes_Rezept_ergibt_nicht_vorhanden_auch_ohne_Stand()
    {
        var id = await AnlegenAsync(Testbenutzer.InhaberA);

        (await new EntwurfFreigebenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberB).HandleAsync(id, null, _ct)).ShouldBeNull();
        (await new EntwurfVerwerfenHandler(_repository, Testbenutzer.InhaberB).HandleAsync(id, null, _ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Konditor_kann_eine_Freigabe_auch_nicht_ueber_einen_veralteten_Stand_erzwingen()
    {
        var id = await AnlegenAsync(Testbenutzer.KonditorA);

        var verweigert = await Should.ThrowAsync<ZugriffVerweigertException>(
            () => new EntwurfFreigebenHandler(_repository, Testzeit.Anbieter, Testbenutzer.KonditorA).HandleAsync(id, null, _ct));

        verweigert.Code.ShouldBe("recht.fehlt");
        _repository.Gespeichert.Values.Single().Entwurf.ShouldNotBeNull();
    }

    [Fact]
    public async Task Mehrere_Rollen_im_selben_Betrieb_ergeben_die_Summe_der_Rechte()
    {
        var mitarbeiterUndKonditor = Testbenutzer.Fuer(Testbetrieb.A, Rolle.Mitarbeiter, Rolle.Konditor);
        var id = await AnlegenAsync(mitarbeiterUndKonditor);

        var bearbeitet = await new EntwurfBearbeitenHandler(_repository, mitarbeiterUndKonditor)
            .HandleAsync(new EntwurfBearbeiten(id, Zeilen(100), _repository.Stand(id)), _ct);

        bearbeitet.ShouldNotBeNull().Zutaten.Count.ShouldBe(100);
        await Should.ThrowAsync<ZugriffVerweigertException>(
            () => new EntwurfFreigebenHandler(_repository, Testzeit.Anbieter, mitarbeiterUndKonditor).HandleAsync(id, _repository.Stand(id), _ct));
    }

    [Fact]
    public async Task Betrieb_ohne_Rollen_hat_kein_Recht()
    {
        // Betrieb bekannt, aber keine Rolle: darf nichts, auch nicht lesen.
        var id = await AnlegenAsync(Testbenutzer.InhaberA);
        var ohneRollen = Testbenutzer.Fuer(Testbetrieb.A);

        var verweigert = await Should.ThrowAsync<ZugriffVerweigertException>(
            () => new RezeptAbfragenHandler(_repository, ohneRollen).HandleAsync(id, _ct));

        verweigert.Code.ShouldBe("recht.fehlt");
    }
}
