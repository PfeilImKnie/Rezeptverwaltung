using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Tests;

/// <summary>Unabhängiger Test (pr-test): Migration ohne Datenverlust und Nebenläufigkeit bei Fassungen.</summary>
public sealed class FassungenPersistenzGrenzfallTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Initial = "20261123130744_Initial";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static Zutatenzeile Zeile(string bezeichnung, decimal gramm) => Zutatenzeile.Von(bezeichnung, Gewicht.Erfasst(gramm));

    private async Task<string> AlteDatenbankMitAsync(params (Guid Id, string Name, string ZutatenJson)[] rezepte)
    {
        var verbindung = postgres.NeueDatenbank();
        await using var alt = postgres.NeuerKontext(verbindung);
        await alt.GetService<IMigrator>().MigrateAsync(Initial, _ct);
        foreach (var (id, name, zutaten) in rezepte)
        {
            await alt.Database.ExecuteSqlAsync($"INSERT INTO rezeptur.rezepte (id, name, zutaten) VALUES ({id}, {name}, {zutaten}::jsonb)", _ct);
        }

        await alt.Database.MigrateAsync(_ct);
        return verbindung;
    }

    private async Task<Rezept> AnlegenAsync(Rezept rezept)
    {
        await using var kontext = postgres.NeuerKontext();
        await new EfRezeptRepository(kontext).HinzufuegenAsync(rezept, _ct);
        return rezept;
    }

    private async Task<Rezept> NeuLadenAsync(RezeptId id, string? verbindung = null)
    {
        // Seit ADR 0015: Migrierte Altdaten (eigene Datenbank) gehören dem Betrieb „altbestand“.
        await using var kontext = postgres.NeuerKontext(verbindung, verbindung is null ? null : Testbenutzer.InhaberAltbestand);
        return (await new EfRezeptRepository(kontext).LadenAsync(id, _ct))!;
    }

    [Fact]
    public async Task Migration_erhaelt_Duplikate_Grenzwerte_und_Reihenfolge_aller_Rezepte()
    {
        var baiser = Guid.NewGuid();
        var gross = Guid.NewGuid();
        var verbindung = await AlteDatenbankMitAsync(
            (baiser, "Baiser", """[{"bezeichnung":"Zucker","gramm":0.01},{"bezeichnung":"Eiweiß","gramm":100},{"bezeichnung":"Zucker","gramm":0.01}]"""),
            (gross, "Großbäckerei", """[{"bezeichnung":"Mehl","gramm":1000000000},{"bezeichnung":"Salz","gramm":999999999.99}]"""));

        var a = await NeuLadenAsync(new RezeptId(baiser), verbindung);
        var b = await NeuLadenAsync(new RezeptId(gross), verbindung);

        a.Name.Wert.ShouldBe("Baiser");
        a.Zutaten.ShouldBe([Zeile("Zucker", 0.01m), Zeile("Eiweiß", 100), Zeile("Zucker", 0.01m)]);
        b.Zutaten.ShouldBe([Zeile("Mehl", 1_000_000_000), Zeile("Salz", 999_999_999.99m)]);
        b.Gesamtgewicht.Gramm.ShouldBe(1_999_999_999.99m);
        foreach (var fassung in new[] { a.MassgeblicheFassung, b.MassgeblicheFassung })
        {
            fassung.Status.ShouldBe(FassungsStatus.Freigegeben);
            fassung.FreigegebenAm.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(fassung.ErstelltAm);
        }
    }

    [Fact]
    public async Task Migriertes_Rezept_bekommt_als_naechsten_Entwurf_Fassung_2_und_laesst_sich_speichern()
    {
        var id = Guid.NewGuid();
        var verbindung = await AlteDatenbankMitAsync((id, "Mürbeteig", """[{"bezeichnung":"Mehl","gramm":300},{"bezeichnung":"Butter","gramm":200}]"""));

        await using (var kontext = postgres.NeuerKontext(verbindung, Testbenutzer.InhaberAltbestand))
        {
            var repository = new EfRezeptRepository(kontext);
            var rezept = (await repository.LadenAsync(new RezeptId(id), _ct))!;
            rezept.EntwurfAnlegen(Testzeit.Jetzt).Nummer.ShouldBe(2);
            rezept.EntwurfBearbeiten([Zeile("Mehl", 280), Zeile("Butter", 220)]);
            await repository.SpeichernAsync(rezept, _ct);
            rezept.EntwurfFreigeben(Testzeit.Jetzt.AddHours(1));
            await repository.SpeichernAsync(rezept, _ct);
        }

        var geladen = await NeuLadenAsync(new RezeptId(id), verbindung);
        geladen.AktuelleFassung!.Nummer.ShouldBe(2);
        geladen.FassungMitNummer(1)!.Zutaten.ShouldBe([Zeile("Mehl", 300), Zeile("Butter", 200)]);
        geladen.Zutaten.ShouldBe([Zeile("Mehl", 280), Zeile("Butter", 220)]);
    }

    [Fact]
    public async Task Gleichzeitig_angelegte_Entwuerfe_ergeben_einen_Konflikt_und_hoechstens_einen_Entwurf()
    {
        var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Sachermasse"), [Zeile("Butter", 140)], Testzeit.Jetzt);
        rezept.EntwurfFreigeben(Testzeit.Jetzt);
        await AnlegenAsync(rezept);

        await using var tablet = postgres.NeuerKontext();
        await using var alexa = postgres.NeuerKontext();
        var ausTablet = (await new EfRezeptRepository(tablet).LadenAsync(rezept.Id, _ct))!;
        var ausAlexa = (await new EfRezeptRepository(alexa).LadenAsync(rezept.Id, _ct))!;

        ausTablet.EntwurfAnlegen(Testzeit.Jetzt);
        await new EfRezeptRepository(tablet).SpeichernAsync(ausTablet, _ct);
        ausAlexa.EntwurfAnlegen(Testzeit.Jetzt);
        var verletzung = await Should.ThrowAsync<DomainRuleViolationException>(() => new EfRezeptRepository(alexa).SpeichernAsync(ausAlexa, _ct));

        verletzung.Code.ShouldBe("rezept.gleichzeitig_geaendert");
        var geladen = await NeuLadenAsync(rezept.Id);
        geladen.Fassungen.Count(f => f.IstEntwurf).ShouldBe(1);
        geladen.LetzteFassungsnummer.ShouldBe(2);
    }

    [Fact]
    public async Task Freigabe_auf_veraltetem_Stand_ueberschreibt_eine_inzwischen_gespeicherte_Bearbeitung_nicht()
    {
        // Alexa gibt frei, während am Tablet der Entwurf noch geändert wurde: Freigegeben würde sonst ein Stand,
        // den niemand gesehen hat, oder die Tablet-Änderung verschwände still.
        var rezept = await AnlegenAsync(Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Ganache"), [Zeile("Kuvertüre", 200)], Testzeit.Jetzt));

        await using var tablet = postgres.NeuerKontext();
        await using var alexa = postgres.NeuerKontext();
        var ausTablet = (await new EfRezeptRepository(tablet).LadenAsync(rezept.Id, _ct))!;
        var ausAlexa = (await new EfRezeptRepository(alexa).LadenAsync(rezept.Id, _ct))!;

        ausTablet.EntwurfBearbeiten([Zeile("Kuvertüre", 200), Zeile("Sahne", 200)]);
        await new EfRezeptRepository(tablet).SpeichernAsync(ausTablet, _ct);
        ausAlexa.EntwurfFreigeben(Testzeit.Jetzt);
        await Should.ThrowAsync<DomainRuleViolationException>(() => new EfRezeptRepository(alexa).SpeichernAsync(ausAlexa, _ct));

        var geladen = await NeuLadenAsync(rezept.Id);
        geladen.Entwurf.ShouldNotBeNull().Zutaten.Count.ShouldBe(2);
        geladen.AktuelleFassung.ShouldBeNull();
    }

    [Fact]
    public async Task Nacheinander_geladene_Aenderungen_ergeben_keinen_falschen_Konflikt()
    {
        var rezept = await AnlegenAsync(Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Biskuit"), [Zeile("Eier", 300)], Testzeit.Jetzt));

        for (var i = 1; i <= 3; i++)
        {
            await using var kontext = postgres.NeuerKontext();
            var repository = new EfRezeptRepository(kontext);
            var geladen = (await repository.LadenAsync(rezept.Id, _ct))!;
            geladen.EntwurfBearbeiten([Zeile("Eier", 300 + i)]);
            await repository.SpeichernAsync(geladen, _ct);
        }

        (await NeuLadenAsync(rezept.Id)).Zutaten.ShouldHaveSingleItem().Menge.ShouldBe(Gewicht.Erfasst(303));
    }

    [Fact]
    public async Task Zeitpunkte_mit_Bruchteilen_einer_Mikrosekunde_bleiben_beim_Speichern_exakt_erhalten()
    {
        // TimeProvider.System liefert 100-ns-Ticks. Die API meldet den Zeitpunkt aus dem Speicher zurück;
        // ein späterer Abruf muss genau denselben Zeitpunkt zeigen.
        var erstellt = Testzeit.Jetzt.AddTicks(1_234_567);
        var freigegeben = Testzeit.Jetzt.AddTicks(9_876_543);
        var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Florentiner"), [Zeile("Mandeln", 200)], erstellt);
        rezept.EntwurfFreigeben(freigegeben);
        await AnlegenAsync(rezept);

        var fassung = (await NeuLadenAsync(rezept.Id)).FassungMitNummer(1)!;

        fassung.ErstelltAm.UtcTicks.ShouldBe(erstellt.UtcTicks);
        fassung.FreigegebenAm!.Value.UtcTicks.ShouldBe(freigegeben.UtcTicks);
    }

    [Fact]
    public async Task Verwerfen_auf_veraltetem_Stand_wird_abgewiesen()
    {
        var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Linzer"), [Zeile("Mehl", 300)], Testzeit.Jetzt);
        rezept.EntwurfFreigeben(Testzeit.Jetzt);
        rezept.EntwurfAnlegen(Testzeit.Jetzt);
        await AnlegenAsync(rezept);

        await using var tablet = postgres.NeuerKontext();
        await using var alexa = postgres.NeuerKontext();
        var ausTablet = (await new EfRezeptRepository(tablet).LadenAsync(rezept.Id, _ct))!;
        var ausAlexa = (await new EfRezeptRepository(alexa).LadenAsync(rezept.Id, _ct))!;

        ausTablet.EntwurfBearbeiten([Zeile("Mehl", 320)]);
        await new EfRezeptRepository(tablet).SpeichernAsync(ausTablet, _ct);
        ausAlexa.EntwurfVerwerfen();
        await Should.ThrowAsync<DomainRuleViolationException>(() => new EfRezeptRepository(alexa).SpeichernAsync(ausAlexa, _ct));

        (await NeuLadenAsync(rezept.Id)).Entwurf.ShouldNotBeNull().Zutaten.ShouldHaveSingleItem().Menge.ShouldBe(Gewicht.Erfasst(320));
    }
}
