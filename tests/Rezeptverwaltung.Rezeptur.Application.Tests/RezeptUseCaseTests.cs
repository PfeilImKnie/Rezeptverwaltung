using Rezeptverwaltung.Rezeptur.Application.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Application.Tests;

public sealed class RezeptUseCaseTests
{
    private readonly InMemoryRezeptRepositoryFake _repository = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private Task<RezeptDetails> SachermasseAnlegenAsync() =>
        new RezeptAnlegenHandler(_repository, Testzeit.Anbieter).HandleAsync(
            new RezeptAnlegen(
                "Sachermasse",
                [new ZutatEingabe("Butter", 140), new ZutatEingabe("Zucker", 140), new ZutatEingabe("Eier", 280.5m)]),
            _ct);

    [Fact]
    public async Task RezeptAnlegen_speichert_das_Rezept_und_liefert_Details()
    {
        var details = await SachermasseAnlegenAsync();

        _repository.Gespeichert.ShouldHaveSingleItem().Value.Name.Wert.ShouldBe("Sachermasse");
        details.Name.ShouldBe("Sachermasse");
        details.Zutaten.ShouldBe([new ZutatMenge("Butter", 140), new ZutatMenge("Zucker", 140), new ZutatMenge("Eier", 280.5m)]);
        details.GesamtgewichtGramm.ShouldBe(560.5m);
    }

    [Fact]
    public async Task RezeptAnlegen_speichert_nichts_bei_verletzter_Fachregel()
    {
        var handler = new RezeptAnlegenHandler(_repository, Testzeit.Anbieter);

        await Should.ThrowAsync<DomainRuleViolationException>(
            () => handler.HandleAsync(new RezeptAnlegen("Fehler", [new ZutatEingabe("Salz", 0.001m)]), _ct));

        _repository.Gespeichert.ShouldBeEmpty();
    }

    [Fact]
    public async Task RezeptAbfragen_liefert_gespeichertes_Rezept()
    {
        var angelegt = await SachermasseAnlegenAsync();

        var details = await new RezeptAbfragenHandler(_repository).HandleAsync(angelegt.Id, _ct);

        details.ShouldBe(angelegt, new RezeptDetailsVergleich());
    }

    [Fact]
    public async Task RezeptAbfragen_liefert_null_fuer_unbekannte_Id() =>
        (await new RezeptAbfragenHandler(_repository).HandleAsync(Guid.NewGuid(), _ct)).ShouldBeNull();

    [Fact]
    public async Task RezeptSkalieren_rundet_Mengen_auf_hundertstel_Gramm()
    {
        var angelegt = await SachermasseAnlegenAsync();

        var skaliert = await new RezeptSkalierenHandler(_repository).HandleAsync(new RezeptSkalieren(angelegt.Id, 1000), _ct);

        skaliert.ShouldNotBeNull();
        skaliert.ZielgewichtGramm.ShouldBe(1000m);
        // 140 / 560,5 * 1000 = 249,77698...  280,5 / 560,5 * 1000 = 500,44603...
        skaliert.Zutaten.Select(z => z.Gramm).ShouldBe([249.78m, 249.78m, 500.45m]);
    }

    [Fact]
    public async Task RezeptSkalieren_liefert_null_fuer_unbekannte_Id() =>
        (await new RezeptSkalierenHandler(_repository).HandleAsync(new RezeptSkalieren(Guid.NewGuid(), 100), _ct)).ShouldBeNull();

    [Fact]
    public async Task RezeptSkalieren_lehnt_zu_genaues_Zielgewicht_ab()
    {
        var angelegt = await SachermasseAnlegenAsync();

        (await Should.ThrowAsync<DomainRuleViolationException>(
                () => new RezeptSkalierenHandler(_repository).HandleAsync(new RezeptSkalieren(angelegt.Id, 100.001m), _ct)))
            .Code.ShouldBe("gewicht.zu_genau");
    }

    [Fact]
    public void ZutatMenge_rundet_kaufmaennisch() =>
        decimal.Round(0.005m, ZutatMenge.AnzeigeNachkommastellen, MidpointRounding.AwayFromZero).ShouldBe(0.01m);

    private sealed class RezeptDetailsVergleich : IEqualityComparer<RezeptDetails>
    {
        public bool Equals(RezeptDetails? x, RezeptDetails? y) =>
            x is not null && y is not null
            && x.Id == y.Id && x.Name == y.Name && x.GesamtgewichtGramm == y.GesamtgewichtGramm
            && x.Zutaten.SequenceEqual(y.Zutaten);

        public int GetHashCode(RezeptDetails obj) => obj.Id.GetHashCode();
    }
}
