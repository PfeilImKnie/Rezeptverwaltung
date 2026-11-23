using Microsoft.EntityFrameworkCore;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Tests;

/// <summary>Mandantentrennung direkt an der Datenbank (ADR 0015), unabhängig von den Prüfungen der Use Cases.</summary>
public sealed class MandantenfilterTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<Rezept> AnlegenAsync(BetriebId betrieb, string name)
    {
        var rezept = Rezept.Anlegen(betrieb, RezeptName.Von(name), [Zutatenzeile.Von("Mehl", Gewicht.Erfasst(100))], Testzeit.Jetzt);
        await using var kontext = postgres.NeuerKontext(benutzer: Testbenutzer.Fuer(betrieb, Rolle.Inhaber));
        await new EfRezeptRepository(kontext).HinzufuegenAsync(rezept, _ct);
        return rezept;
    }

    [Fact]
    public async Task Betrieb_B_findet_das_Rezept_von_Betrieb_A_nicht()
    {
        var rezept = await AnlegenAsync(Testbetrieb.A, "Linzer");

        await using var b = postgres.NeuerKontext(benutzer: Testbenutzer.InhaberB);

        (await new EfRezeptRepository(b).LadenAsync(rezept.Id, _ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Jede_Abfrage_ist_gefiltert_nicht_nur_das_Laden_ueber_die_Id()
    {
        var betriebC = BetriebId.Von($"c-{Guid.NewGuid():N}");
        var betriebD = BetriebId.Von($"d-{Guid.NewGuid():N}");
        await AnlegenAsync(betriebC, "C1");
        await AnlegenAsync(betriebC, "C2");
        await AnlegenAsync(betriebD, "D1");

        await using var c = postgres.NeuerKontext(benutzer: Testbenutzer.Fuer(betriebC, Rolle.Mitarbeiter));

        (await c.Rezepte.Select(r => r.Name).ToListAsync(_ct)).Select(n => n.Wert).ShouldBe(["C1", "C2"], ignoreOrder: true);
        (await c.Rezepte.CountAsync(_ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Ohne_Betrieb_liefert_die_Datenbank_gar_nichts()
    {
        await AnlegenAsync(Testbetrieb.A, "Sichtbar nur für A");

        await using var ohne = postgres.NeuerKontext(benutzer: Testbenutzer.OhneRolle);
        await using var mehrdeutig = postgres.NeuerKontext(benutzer: Testbenutzer.ZweiBetriebe);

        (await ohne.Rezepte.AnyAsync(_ct)).ShouldBeFalse();
        (await mehrdeutig.Rezepte.AnyAsync(_ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Rezept_eines_anderen_Betriebs_kann_nicht_angelegt_werden()
    {
        var fremd = Rezept.Anlegen(Testbetrieb.B, RezeptName.Von("Untergeschoben"), [], Testzeit.Jetzt);
        await using var a = postgres.NeuerKontext(benutzer: Testbenutzer.InhaberA);

        await Should.ThrowAsync<InvalidOperationException>(() => new EfRezeptRepository(a).HinzufuegenAsync(fremd, _ct));
    }

    [Fact]
    public async Task Ohne_Anmeldung_kann_nichts_angelegt_werden()
    {
        var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Anonym"), [], Testzeit.Jetzt);
        await using var ohne = postgres.NeuerKontext(benutzer: Testbenutzer.OhneRolle);

        await Should.ThrowAsync<InvalidOperationException>(() => new EfRezeptRepository(ohne).HinzufuegenAsync(rezept, _ct));
    }

    [Fact]
    public async Task Gleichzeitige_Kontexte_verschiedener_Betriebe_beeinflussen_sich_nicht()
    {
        // Der Filter nutzt den Betrieb der jeweiligen Kontext-Instanz, nicht den des ersten erzeugten Kontexts (Modell-Cache).
        var rezeptA = await AnlegenAsync(Testbetrieb.A, "Nur A");
        var rezeptB = await AnlegenAsync(Testbetrieb.B, "Nur B");

        await using var a = postgres.NeuerKontext(benutzer: Testbenutzer.InhaberA);
        await using var b = postgres.NeuerKontext(benutzer: Testbenutzer.InhaberB);
        // Abwechselnd über beide Kontexte, damit ein falsch zwischengespeicherter Filter sofort auffiele.
        var ausA = new[] { await new EfRezeptRepository(a).LadenAsync(rezeptA.Id, _ct), null };
        var ausB = new[] { await new EfRezeptRepository(b).LadenAsync(rezeptA.Id, _ct), null };
        ausA[1] = await new EfRezeptRepository(a).LadenAsync(rezeptB.Id, _ct);
        ausB[1] = await new EfRezeptRepository(b).LadenAsync(rezeptB.Id, _ct);

        ausA[0].ShouldNotBeNull();
        ausA[1].ShouldBeNull();
        ausB[0].ShouldBeNull();
        ausB[1].ShouldNotBeNull();
    }

    [Fact]
    public async Task Betrieb_wird_gespeichert_und_geladen()
    {
        var rezept = await AnlegenAsync(Testbetrieb.B, "Betriebsprobe");

        await using var b = postgres.NeuerKontext(benutzer: Testbenutzer.InhaberB);

        (await new EfRezeptRepository(b).LadenAsync(rezept.Id, _ct))!.BetriebId.ShouldBe(Testbetrieb.B);
    }
}
