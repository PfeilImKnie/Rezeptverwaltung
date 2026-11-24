using Rezeptverwaltung.Rezeptur.Application.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Application.Tests;

/// <summary>Rechte je Rolle und Mandantentrennung in den Use Cases, ohne Datenbank (ADR 0015).</summary>
public sealed class RechteUseCaseTests
{
    private readonly InMemoryRezeptRepositoryFake _repository = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<Guid> AnlegenAsync(IAktuellerBenutzer benutzer) =>
        (await new RezeptAnlegenHandler(_repository, Testzeit.Anbieter, benutzer)
            .HandleAsync(new RezeptAnlegen("Biskuit", [new ZutatEingabe("Eier", 300)], Form.Ring(26, 6)), _ct)).Id;

    public static TheoryData<string, bool, bool, bool> Matrix => new()
    {
        // Rolle,           lesen, bearbeiten, freigeben
        { nameof(Testbenutzer.MitarbeiterA), true, false, false },
        { nameof(Testbenutzer.KonditorA), true, true, false },
        { nameof(Testbenutzer.InhaberA), true, true, true },
        { nameof(Testbenutzer.OhneRolle), false, false, false },
        { nameof(Testbenutzer.ZweiBetriebe), false, false, false },
    };

    private static Testbenutzer Benutzer(string name) =>
        (Testbenutzer)typeof(Testbenutzer).GetField(name)!.GetValue(null)!;

    private static async Task<bool> ErlaubtAsync(Func<Task> aktion)
    {
        try
        {
            await aktion();
            return true;
        }
        catch (ZugriffVerweigertException)
        {
            return false;
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Rechte_je_Rolle(string rolle, bool lesen, bool bearbeiten, bool freigeben)
    {
        var benutzer = Benutzer(rolle);
        var id = await AnlegenAsync(Testbenutzer.InhaberA);

        (await ErlaubtAsync(() => new RezeptAbfragenHandler(_repository, benutzer).HandleAsync(id, _ct))).ShouldBe(lesen, "lesen");
        (await ErlaubtAsync(() => new FassungAbfragenHandler(_repository, benutzer).HandleAsync(id, 1, _ct))).ShouldBe(lesen, "Fassung lesen");
        (await ErlaubtAsync(() => new RezeptSkalierenHandler(_repository, benutzer).HandleAsync(new RezeptSkalieren(id, 100), _ct))).ShouldBe(lesen, "skalieren");
        (await ErlaubtAsync(() => new RezeptAufFormSkalierenHandler(_repository, benutzer).HandleAsync(new RezeptAufFormSkalieren(id, Form.Ring(18, 6)), _ct))).ShouldBe(lesen, "umrechnen");
        (await ErlaubtAsync(() => AnlegenAsync(benutzer))).ShouldBe(bearbeiten, "anlegen");
        (await ErlaubtAsync(() => new EntwurfBearbeitenHandler(_repository, benutzer)
            .HandleAsync(new EntwurfBearbeiten(id, [new ZutatEingabe("Eier", 310)], _repository.Stand(id)), _ct))).ShouldBe(bearbeiten, "bearbeiten");
        (await ErlaubtAsync(() => new EntwurfFreigebenHandler(_repository, Testzeit.Anbieter, benutzer).HandleAsync(id, _repository.Stand(id), _ct))).ShouldBe(freigeben, "freigeben");
    }

    public static TheoryData<string, bool> Bearbeitungsrecht => new()
    {
        { nameof(Testbenutzer.MitarbeiterA), false },
        { nameof(Testbenutzer.KonditorA), true },
        { nameof(Testbenutzer.InhaberA), true },
        { nameof(Testbenutzer.OhneRolle), false },
        { nameof(Testbenutzer.ZweiBetriebe), false },
    };

    [Theory]
    [MemberData(nameof(Bearbeitungsrecht))]
    public async Task Entwurf_anlegen_und_verwerfen_brauchen_das_Recht_zu_bearbeiten(string rolle, bool bearbeiten)
    {
        var benutzer = Benutzer(rolle);
        var id = await AnlegenAsync(Testbenutzer.InhaberA);
        await new EntwurfFreigebenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(id, _repository.Stand(id), _ct);

        (await ErlaubtAsync(() => new EntwurfAnlegenHandler(_repository, Testzeit.Anbieter, benutzer).HandleAsync(id, _repository.Stand(id), _ct))).ShouldBe(bearbeiten, "Entwurf anlegen");
        if (bearbeiten)
        {
            (await ErlaubtAsync(() => new EntwurfVerwerfenHandler(_repository, benutzer).HandleAsync(id, _repository.Stand(id), _ct))).ShouldBeTrue("verwerfen");
        }
        else
        {
            (await ErlaubtAsync(() => new EntwurfVerwerfenHandler(_repository, benutzer).HandleAsync(id, _repository.Stand(id), _ct))).ShouldBeFalse("verwerfen");
        }
    }

    [Fact]
    public async Task Zwei_Betriebe_ergeben_einen_eigenen_Code()
    {
        var verweigert = await Should.ThrowAsync<ZugriffVerweigertException>(
            () => new RezeptAbfragenHandler(_repository, Testbenutzer.ZweiBetriebe).HandleAsync(Guid.NewGuid(), _ct));

        verweigert.Code.ShouldBe("betrieb.nicht_eindeutig");
    }

    [Fact]
    public async Task Fehlendes_Recht_hat_einen_verstaendlichen_Code_und_Text()
    {
        var verweigert = await Should.ThrowAsync<ZugriffVerweigertException>(
            () => new EntwurfFreigebenHandler(_repository, Testzeit.Anbieter, Testbenutzer.KonditorA).HandleAsync(Guid.NewGuid(), "1", _ct));

        verweigert.Code.ShouldBe("recht.fehlt");
        verweigert.Message.ShouldContain("Entwurf freigeben");
    }

    [Fact]
    public async Task Neues_Rezept_gehoert_dem_Betrieb_der_angemeldeten_Person()
    {
        var id = await AnlegenAsync(Testbenutzer.InhaberB);

        _repository.Gespeichert.Values.Single(r => r.Id.Wert == id).BetriebId.ShouldBe(Testbetrieb.B);
    }

    [Fact]
    public async Task Rezept_eines_anderen_Betriebs_ist_fuer_alle_Use_Cases_nicht_vorhanden()
    {
        // Zweite Verteidigungslinie: Selbst wenn der Speicher ein fremdes Rezept lieferte, bleibt es unsichtbar.
        var id = await AnlegenAsync(Testbenutzer.InhaberA);
        var b = Testbenutzer.InhaberB;

        (await new RezeptAbfragenHandler(_repository, b).HandleAsync(id, _ct)).ShouldBeNull();
        (await new FassungAbfragenHandler(_repository, b).HandleAsync(id, 1, _ct)).ShouldBeNull();
        (await new RezeptSkalierenHandler(_repository, b).HandleAsync(new RezeptSkalieren(id, 100), _ct)).ShouldBeNull();
        (await new RezeptAufFormSkalierenHandler(_repository, b).HandleAsync(new RezeptAufFormSkalieren(id, Form.Ring(18, 6)), _ct)).ShouldBeNull();
        (await new EntwurfBearbeitenHandler(_repository, b).HandleAsync(new EntwurfBearbeiten(id, [], _repository.Stand(id)), _ct)).ShouldBeNull();
        (await new EntwurfFreigebenHandler(_repository, Testzeit.Anbieter, b).HandleAsync(id, _repository.Stand(id), _ct)).ShouldBeNull();
        (await new EntwurfAnlegenHandler(_repository, Testzeit.Anbieter, b).HandleAsync(id, _repository.Stand(id), _ct)).ShouldBeNull();
        (await new EntwurfVerwerfenHandler(_repository, b).HandleAsync(id, _repository.Stand(id), _ct)).ShouldBeNull();
        _repository.Speichervorgaenge.ShouldBe(0);
    }
}
