using Rezeptverwaltung.Rezeptur.Application.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Application.Tests;

/// <summary>Use Case „Rezept skalieren“: Fehlerarten und ihre Reihenfolge (Eingabe vor Zustand).</summary>
public sealed class RezeptSkalierenFehlerTests
{
    private readonly InMemoryRezeptRepositoryFake _repository = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<Guid> LeeresRezeptAnlegenAsync() =>
        (await new RezeptAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(new RezeptAnlegen("Noch leer", []), _ct)).Id;

    private Task<DomainRuleViolationException> SkalierenSchlaegtFehlAsync(Guid id, decimal ziel) =>
        Should.ThrowAsync<DomainRuleViolationException>(
            () => new RezeptSkalierenHandler(_repository, Testbenutzer.InhaberA).HandleAsync(new RezeptSkalieren(id, ziel), _ct));

    [Fact]
    public async Task RezeptAnlegen_ohne_Zutaten_ist_erlaubt()
    {
        var id = await LeeresRezeptAnlegenAsync();

        _repository.Gespeichert.ShouldHaveSingleItem().Value.Id.Wert.ShouldBe(id);
    }

    [Fact]
    public async Task RezeptSkalieren_ohne_Zutaten_ist_ein_Zustandskonflikt()
    {
        var verletzung = await SkalierenSchlaegtFehlAsync(await LeeresRezeptAnlegenAsync(), 100);

        verletzung.Code.ShouldBe("rezept.ohne_zutaten");
        verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
    }

    [Theory]
    [InlineData("0", "skalierung.ziel_null")]
    [InlineData("0.005", "gewicht.zu_genau")]
    [InlineData("-1", "gewicht.negativ")]
    [InlineData("1000000000.01", "gewicht.zu_gross")]
    public async Task RezeptSkalieren_ohne_Zutaten_mit_ungueltigem_Zielgewicht_ist_eine_ungueltige_Eingabe(string ziel, string code)
    {
        var verletzung = await SkalierenSchlaegtFehlAsync(
            await LeeresRezeptAnlegenAsync(),
            decimal.Parse(ziel, System.Globalization.CultureInfo.InvariantCulture));

        verletzung.Code.ShouldBe(code);
        verletzung.Art.ShouldBe(Regelart.UngueltigeEingabe);
    }

    [Fact]
    public async Task RezeptSkalieren_ohne_Zutaten_aendert_das_gespeicherte_Rezept_nicht()
    {
        var id = await LeeresRezeptAnlegenAsync();

        _ = await SkalierenSchlaegtFehlAsync(id, 100);

        _repository.Gespeichert.ShouldHaveSingleItem().Value.Zutaten.ShouldBeEmpty();
    }
}
