using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Tests;

/// <summary>
/// Bestandsdaten müssen jede Migration überstehen (ADR 0011): Datenbank auf einen alten Stand bringen,
/// Daten im alten Format einfügen, auf den neuesten Stand migrieren und über das Repository laden.
/// </summary>
public sealed class DatenmigrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Initial = "20261123130744_Initial";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rezepte_aus_der_Zeit_vor_den_Fassungen_werden_zu_freigegebener_Fassung_1()
    {
        var verbindung = postgres.NeueDatenbank();
        var id = Guid.NewGuid();
        await using (var alt = postgres.NeuerKontext(verbindung))
        {
            await alt.GetService<IMigrator>().MigrateAsync(Initial, _ct);
            await alt.Database.ExecuteSqlAsync(
                $$"""
                INSERT INTO rezeptur.rezepte (id, name, zutaten)
                VALUES ({{id}}, 'Mürbeteig', '[{"bezeichnung": "Zucker", "gramm": 100}, {"bezeichnung": "Butter", "gramm": 200.5}, {"bezeichnung": "Mehl", "gramm": 300}]'::jsonb)
                """,
                _ct);
        }

        await using (var migrieren = postgres.NeuerKontext(verbindung))
        {
            await migrieren.Database.MigrateAsync(_ct);
        }

        // Altdaten gehören seit ADR 0015 dem Betrieb „altbestand“.
        await using var lesen = postgres.NeuerKontext(verbindung, Testbenutzer.InhaberAltbestand);
        var rezept = await new EfRezeptRepository(lesen).LadenAsync(new RezeptId(id), _ct);

        rezept.ShouldNotBeNull();
        var fassung = rezept.Fassungen.ShouldHaveSingleItem();
        fassung.Nummer.ShouldBe(1);
        fassung.Status.ShouldBe(FassungsStatus.Freigegeben);
        fassung.FreigegebenAm.ShouldNotBeNull();
        fassung.Zutaten.Select(z => (z.Bezeichnung, z.Menge.Gramm)).ShouldBe([("Zucker", 100m), ("Butter", 200.5m), ("Mehl", 300m)]);
        rezept.AktuelleFassung.ShouldBe(fassung);
        rezept.LetzteFassungsnummer.ShouldBe(1);
        rezept.Entwurf.ShouldBeNull();
    }

    [Fact]
    public async Task Rezept_ohne_Zutaten_aus_der_Zeit_vor_den_Fassungen_bleibt_ladbar()
    {
        var verbindung = postgres.NeueDatenbank();
        var id = Guid.NewGuid();
        await using (var alt = postgres.NeuerKontext(verbindung))
        {
            await alt.GetService<IMigrator>().MigrateAsync(Initial, _ct);
            await alt.Database.ExecuteSqlAsync($"INSERT INTO rezeptur.rezepte (id, name, zutaten) VALUES ({id}, 'Leer', '[]'::jsonb)", _ct);
            await alt.Database.MigrateAsync(_ct);
        }

        // Altdaten gehören seit ADR 0015 dem Betrieb „altbestand“.
        await using var lesen = postgres.NeuerKontext(verbindung, Testbenutzer.InhaberAltbestand);
        var rezept = await new EfRezeptRepository(lesen).LadenAsync(new RezeptId(id), _ct);

        var fassung = rezept.ShouldNotBeNull().Fassungen.ShouldHaveSingleItem();
        fassung.Zutaten.ShouldBeEmpty();
        fassung.Status.ShouldBe(FassungsStatus.Entwurf, "Eine Fassung ohne Zutaten darf nicht freigegeben sein");
        fassung.FreigegebenAm.ShouldBeNull();
        rezept.Entwurf.ShouldBe(fassung);
    }

    [Fact]
    public async Task Rueckweg_uebernimmt_die_massgebliche_Fassung_nicht_den_Entwurf()
    {
        var verbindung = postgres.NeueDatenbank();
        var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Mürbeteig"), [Zutatenzeile.Von("Mehl", Gewicht.Erfasst(500))], Testzeit.Jetzt);
        rezept.EntwurfFreigeben(Testzeit.Jetzt);
        rezept.EntwurfAnlegen(Testzeit.Jetzt);
        rezept.EntwurfBearbeiten([Zutatenzeile.Von("Mehl", Gewicht.Erfasst(600))]);
        await using (var neu = postgres.NeuerKontext(verbindung))
        {
            await neu.Database.MigrateAsync(_ct);
            await new EfRezeptRepository(neu).HinzufuegenAsync(rezept, _ct);
        }

        await using var alt = postgres.NeuerKontext(verbindung);
        await alt.GetService<IMigrator>().MigrateAsync(Initial, _ct);
        var zutaten = await alt.Database
            .SqlQuery<string>($"SELECT zutaten::text AS \"Value\" FROM rezeptur.rezepte WHERE id = {rezept.Id.Wert}")
            .SingleAsync(_ct);

        using var dokument = System.Text.Json.JsonDocument.Parse(zutaten);
        dokument.RootElement[0].GetProperty("gramm").GetDecimal().ShouldBe(500m);
    }
}
