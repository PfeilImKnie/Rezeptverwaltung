using Rezeptverwaltung.Rezeptur.Application.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Application.Tests;

/// <summary>Rezeptliste mit Suche und Abfrage der eigenen Rechte (Grundlage der Weboberfläche).</summary>
public sealed class RezeptlisteUseCaseTests
{
    private readonly InMemoryRezeptRepositoryFake _repository = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<Guid> AnlegenAsync(string name, IAktuellerBenutzer? benutzer = null) =>
        (await new RezeptAnlegenHandler(_repository, Testzeit.Anbieter, benutzer ?? Testbenutzer.InhaberA)
            .HandleAsync(new RezeptAnlegen(name, [new ZutatEingabe("Eier", 300)]), _ct)).Id;

    private Task<IReadOnlyList<RezeptUebersicht>> AuflistenAsync(string? suche = null, IAktuellerBenutzer? benutzer = null) =>
        new RezepteAuflistenHandler(_repository, benutzer ?? Testbenutzer.MitarbeiterA).HandleAsync(suche, _ct);

    [Fact]
    public async Task Liste_ist_alphabetisch_sortiert()
    {
        await AnlegenAsync("Sachertorte");
        await AnlegenAsync("biskuit");
        await AnlegenAsync("Linzer Torte");

        (await AuflistenAsync()).Select(r => r.Name).ShouldBe(["biskuit", "Linzer Torte", "Sachertorte"]);
    }

    [Fact]
    public async Task Ohne_Rezepte_ist_die_Liste_leer() =>
        (await AuflistenAsync()).ShouldBeEmpty();

    [Fact]
    public async Task Suche_filtert_nach_Namen()
    {
        await AnlegenAsync("Sachertorte");
        await AnlegenAsync("Käsekuchen");
        await AnlegenAsync("Linzer Torte");

        (await AuflistenAsync("torte")).Select(r => r.Name).ShouldBe(["Linzer Torte", "Sachertorte"]);
        (await AuflistenAsync("kase")).Select(r => r.Name).ShouldBe(["Käsekuchen"]);
        (await AuflistenAsync("Mohn")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Eintrag_nennt_Fassungen()
    {
        var id = await AnlegenAsync("Biskuit");
        await new EntwurfFreigebenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(id, _repository.Stand(id), _ct);
        await new EntwurfAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(id, _repository.Stand(id), _ct);

        (await AuflistenAsync()).ShouldHaveSingleItem().ShouldBe(new RezeptUebersicht(id, "Biskuit", 1, 1, 2));
    }

    [Fact]
    public async Task Neues_Rezept_hat_nur_den_Entwurf()
    {
        var id = await AnlegenAsync("Biskuit");

        (await AuflistenAsync()).ShouldHaveSingleItem().ShouldBe(new RezeptUebersicht(id, "Biskuit", 1, null, 1));
    }

    [Fact]
    public async Task Rezepte_anderer_Betriebe_erscheinen_nicht()
    {
        await AnlegenAsync("Sachertorte");
        await AnlegenAsync("Fremde Torte", Testbenutzer.InhaberB);

        (await AuflistenAsync()).Select(r => r.Name).ShouldBe(["Sachertorte"]);
        (await AuflistenAsync(benutzer: Testbenutzer.InhaberB)).Select(r => r.Name).ShouldBe(["Fremde Torte"]);
    }

    [Theory]
    [InlineData(nameof(Testbenutzer.OhneRolle), "recht.fehlt")]
    [InlineData(nameof(Testbenutzer.ZweiBetriebe), "betrieb.nicht_eindeutig")]
    public async Task Ohne_Leserecht_gibt_es_keine_Liste(string name, string code)
    {
        var benutzer = (Testbenutzer)typeof(Testbenutzer).GetField(name)!.GetValue(null)!;

        (await Should.ThrowAsync<ZugriffVerweigertException>(() => AuflistenAsync(benutzer: benutzer))).Code.ShouldBe(code);
    }

    [Fact]
    public async Task Recht_wird_vor_der_Suche_geprueft() =>
        await Should.ThrowAsync<ZugriffVerweigertException>(() => AuflistenAsync(new string('a', 500), Testbenutzer.OhneRolle));

    [Fact]
    public async Task Zu_lange_Suche_ist_ungueltig() =>
        (await Should.ThrowAsync<DomainRuleViolationException>(() => AuflistenAsync(new string('a', 101)))).Code.ShouldBe("suche.zu_lang");

    public static TheoryData<string, bool, bool, bool> Matrix => new()
    {
        { nameof(Testbenutzer.MitarbeiterA), true, false, false },
        { nameof(Testbenutzer.KonditorA), true, true, false },
        { nameof(Testbenutzer.InhaberA), true, true, true },
        { nameof(Testbenutzer.OhneRolle), false, false, false },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Berechtigungen_je_Rolle(string name, bool lesen, bool bearbeiten, bool freigeben)
    {
        var benutzer = (Testbenutzer)typeof(Testbenutzer).GetField(name)!.GetValue(null)!;

        new BerechtigungenAbfragenHandler(benutzer).Handle().ShouldBe(new Berechtigungen(lesen, bearbeiten, freigeben));
    }

    [Fact]
    public void Mehrere_Rollen_addieren_sich() =>
        new BerechtigungenAbfragenHandler(Testbenutzer.Fuer(Testbetrieb.A, Rolle.Mitarbeiter, Rolle.Inhaber)).Handle()
            .ShouldBe(new Berechtigungen(true, true, true));

    [Fact]
    public void Rollen_in_mehreren_Betrieben_ergeben_403() =>
        Should.Throw<ZugriffVerweigertException>(() => new BerechtigungenAbfragenHandler(Testbenutzer.ZweiBetriebe).Handle())
            .Code.ShouldBe("betrieb.nicht_eindeutig");
}
