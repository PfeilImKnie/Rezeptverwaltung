using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Tests;

/// <summary>
/// Akzeptanzkriterium (5): Bestehende Rezepte bleiben erhalten und gehören nach der Migration dem Betrieb „altbestand“.
/// Ausgangspunkt ist der Stand unmittelbar vor der Migration (Fassungen, ADR 0013), wie er im Betrieb vorliegt.
/// </summary>
public sealed class BetriebeMigrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Initial = "20261123130744_Initial";
    private const string Fassungen = "20261123141130_Fassungen";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Legt eine Datenbank auf dem Stand „Fassungen“ mit zwei Rezepten an und migriert dann auf den neuesten Stand.</summary>
    private async Task<(string Verbindung, Guid Freigegeben, Guid MitEntwurf)> BestandMigrierenAsync()
    {
        var verbindung = postgres.NeueDatenbank();
        var freigegeben = Guid.NewGuid();
        var mitEntwurf = Guid.NewGuid();
        await using (var alt = postgres.NeuerKontext(verbindung))
        {
            await alt.GetService<IMigrator>().MigrateAsync(Initial, _ct);
            await alt.Database.ExecuteSqlAsync(
                $$"""
                INSERT INTO rezeptur.rezepte (id, name, zutaten)
                VALUES ({{freigegeben}}, 'Sachertorte', '[{"bezeichnung": "Kuvertüre", "gramm": 150}, {"bezeichnung": "Butter", "gramm": 140.25}]'::jsonb)
                """,
                _ct);
            await alt.GetService<IMigrator>().MigrateAsync(Fassungen, _ct);

            // Ein Rezept, das bereits im Format „Fassungen“ entstanden ist: Fassung 1 freigegeben, Fassung 2 als Entwurf.
            await alt.Database.ExecuteSqlAsync(
                $$"""
                INSERT INTO rezeptur.rezepte (id, name, letzte_fassungsnummer, fassungen)
                VALUES ({{mitEntwurf}}, 'Linzer Torte', 2, '[
                  {"nummer": 1, "status": "Freigegeben", "erstelltAm": "2026-11-01T08:00:00+00:00", "freigegebenAm": "2026-11-01T09:00:00+00:00",
                   "zutaten": [{"bezeichnung": "Mehl", "gramm": 250}]},
                  {"nummer": 2, "status": "Entwurf", "erstelltAm": "2026-11-02T08:00:00+00:00", "freigegebenAm": null,
                   "zutaten": [{"bezeichnung": "Mehl", "gramm": 300}]}
                ]'::jsonb)
                """,
                _ct);
        }

        await using (var migrieren = postgres.NeuerKontext(verbindung))
        {
            await migrieren.Database.MigrateAsync(_ct);
        }

        return (verbindung, freigegeben, mitEntwurf);
    }

    [Fact]
    public async Task Bestehende_Rezepte_bleiben_mit_allen_Fassungen_erhalten_und_gehoeren_dem_Altbestand()
    {
        var (verbindung, freigegeben, mitEntwurf) = await BestandMigrierenAsync();

        await using var lesen = postgres.NeuerKontext(verbindung, Testbenutzer.InhaberAltbestand);
        var sacher = (await new EfRezeptRepository(lesen).LadenAsync(new RezeptId(freigegeben), _ct)).ShouldNotBeNull();
        var linzer = (await new EfRezeptRepository(lesen).LadenAsync(new RezeptId(mitEntwurf), _ct)).ShouldNotBeNull();

        sacher.BetriebId.ShouldBe(Testbetrieb.Altbestand);
        sacher.Name.Wert.ShouldBe("Sachertorte");
        sacher.Zutaten.Select(z => (z.Bezeichnung, z.Menge.Gramm)).ShouldBe([("Kuvertüre", 150m), ("Butter", 140.25m)]);
        linzer.BetriebId.ShouldBe(Testbetrieb.Altbestand);
        linzer.Fassungen.Count.ShouldBe(2);
        linzer.AktuelleFassung!.Nummer.ShouldBe(1);
        linzer.Entwurf!.Nummer.ShouldBe(2);
        linzer.Entwurf.Zutaten.ShouldHaveSingleItem().Menge.Gramm.ShouldBe(300m);
    }

    [Fact]
    public async Task Altbestand_ist_fuer_echte_Betriebe_nicht_sichtbar()
    {
        var (verbindung, freigegeben, mitEntwurf) = await BestandMigrierenAsync();

        await using var a = postgres.NeuerKontext(verbindung, Testbenutzer.InhaberA);
        await using var ohne = postgres.NeuerKontext(verbindung, Testbenutzer.OhneRolle);

        (await new EfRezeptRepository(a).LadenAsync(new RezeptId(freigegeben), _ct)).ShouldBeNull();
        (await new EfRezeptRepository(a).LadenAsync(new RezeptId(mitEntwurf), _ct)).ShouldBeNull();
        (await a.Rezepte.AnyAsync(_ct)).ShouldBeFalse();
        (await ohne.Rezepte.AnyAsync(_ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Alle_Bestandsrezepte_stehen_in_der_Datenbank_beim_Altbestand()
    {
        var (verbindung, _, _) = await BestandMigrierenAsync();

        await using var kontext = postgres.NeuerKontext(verbindung);
        var betriebe = await kontext.Database
            .SqlQuery<string>($"SELECT betrieb_id AS \"Value\" FROM rezeptur.rezepte")
            .ToListAsync(_ct);

        betriebe.ShouldBe(["altbestand", "altbestand"]);
    }

    [Fact]
    public async Task Nach_der_Migration_gibt_es_keinen_stillen_Standardbetrieb_fuer_neue_Rezepte()
    {
        var (verbindung, _, _) = await BestandMigrierenAsync();

        await using var kontext = postgres.NeuerKontext(verbindung);
        var fehler = await Should.ThrowAsync<PostgresException>(() => kontext.Database.ExecuteSqlAsync(
            $"INSERT INTO rezeptur.rezepte (id, name, letzte_fassungsnummer, fassungen) VALUES ({Guid.NewGuid()}, 'Ohne Betrieb', 1, '[]'::jsonb)",
            _ct));

        fehler.SqlState.ShouldBe(PostgresErrorCodes.NotNullViolation);
    }

    [Fact]
    public async Task Altbestand_kann_per_SQL_einem_echten_Betrieb_zugeordnet_werden()
    {
        var (verbindung, freigegeben, _) = await BestandMigrierenAsync();
        await using (var kontext = postgres.NeuerKontext(verbindung))
        {
            await kontext.Database.ExecuteSqlAsync(
                $"UPDATE rezeptur.rezepte SET betrieb_id = {Testbetrieb.A.Wert} WHERE betrieb_id = 'altbestand'",
                _ct);
        }

        await using var a = postgres.NeuerKontext(verbindung, Testbenutzer.InhaberA);
        await using var altbestand = postgres.NeuerKontext(verbindung, Testbenutzer.InhaberAltbestand);

        (await new EfRezeptRepository(a).LadenAsync(new RezeptId(freigegeben), _ct)).ShouldNotBeNull().BetriebId.ShouldBe(Testbetrieb.A);
        (await altbestand.Rezepte.AnyAsync(_ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Rueckweg_entfernt_den_Betrieb_ohne_Rezepte_zu_verlieren()
    {
        var (verbindung, _, _) = await BestandMigrierenAsync();

        await using var kontext = postgres.NeuerKontext(verbindung);
        await kontext.GetService<IMigrator>().MigrateAsync(Fassungen, _ct);
        var anzahl = await kontext.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM rezeptur.rezepte").SingleAsync(_ct);

        anzahl.ShouldBe(2);
    }
}
