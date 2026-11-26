using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Tests;

/// <summary>
/// Unabhängiger Test von <c>IRezeptRepository.AlleAsync</c> (Grundlage der Rezeptliste) gegen echtes PostgreSQL:
/// Die Datenbank liefert nur Rezepte des eigenen Betriebs, unabhängig von der Prüfung im Use Case.
/// </summary>
public sealed class RezeptlistePersistenzTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task AnlegenAsync(BetriebId betrieb, string name, params (string Bezeichnung, decimal Gramm)[] zutaten)
    {
        var zeilen = zutaten.Select(z => Zutatenzeile.Von(z.Bezeichnung, Gewicht.Erfasst(z.Gramm))).ToList();
        var rezept = Rezept.Anlegen(betrieb, RezeptName.Von(name), zeilen, Testzeit.Jetzt);
        await using var kontext = postgres.NeuerKontext(benutzer: Testbenutzer.Fuer(betrieb, Rolle.Inhaber));
        await new EfRezeptRepository(kontext).HinzufuegenAsync(rezept, _ct);
    }

    [Fact]
    public async Task AlleAsync_liefert_nur_die_Rezepte_des_eigenen_Betriebs()
    {
        var eigener = BetriebId.Von($"e-{Guid.NewGuid():N}");
        var fremder = BetriebId.Von($"f-{Guid.NewGuid():N}");
        await AnlegenAsync(eigener, "Sachertorte", ("Butter", 140m));
        await AnlegenAsync(eigener, "Linzer Torte", ("Mehl", 250m));
        await AnlegenAsync(fremder, "Fremde Torte", ("Zucker", 100m));

        await using var kontext = postgres.NeuerKontext(benutzer: Testbenutzer.Fuer(eigener, Rolle.Mitarbeiter));
        var rezepte = await new EfRezeptRepository(kontext).AlleAsync(_ct);

        rezepte.Select(r => r.Name.Wert).ShouldBe(["Sachertorte", "Linzer Torte"], ignoreOrder: true);
        rezepte.ShouldAllBe(r => r.BetriebId == eigener);
    }

    [Fact]
    public async Task AlleAsync_ohne_eindeutigen_Betrieb_liefert_nichts()
    {
        await AnlegenAsync(Testbetrieb.A, "Nur für A", ("Mehl", 1m));

        await using var ohne = postgres.NeuerKontext(benutzer: Testbenutzer.OhneRolle);
        await using var mehrdeutig = postgres.NeuerKontext(benutzer: Testbenutzer.ZweiBetriebe);

        (await new EfRezeptRepository(ohne).AlleAsync(_ct)).ShouldBeEmpty();
        (await new EfRezeptRepository(mehrdeutig).AlleAsync(_ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AlleAsync_liefert_vollstaendige_Rezepte_mit_Zutaten_in_der_Reihenfolge()
    {
        var betrieb = BetriebId.Von($"v-{Guid.NewGuid():N}");
        await AnlegenAsync(betrieb, "Biskuit", ("Eier", 300m), ("Zucker", 150m), ("Zucker", 0.01m), ("Mehl", 150m));

        await using var kontext = postgres.NeuerKontext(benutzer: Testbenutzer.Fuer(betrieb, Rolle.Mitarbeiter));
        var rezept = (await new EfRezeptRepository(kontext).AlleAsync(_ct)).ShouldHaveSingleItem();

        rezept.MassgeblicheFassung.Nummer.ShouldBe(1);
        rezept.AktuelleFassung.ShouldBeNull();
        rezept.Gesamtgewicht.Gramm.ShouldBe(600.01m);
    }
}
