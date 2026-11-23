using Microsoft.EntityFrameworkCore;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Tests;

public sealed class BezugsformPersistenzTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<Rezept> SpeichernUndLadenAsync(Bezugsform? form)
    {
        var rezept = Rezept.Anlegen(RezeptName.Von("Biskuit"), [Zutatenzeile.Von("Eier", Gewicht.Erfasst(300))], Testzeit.Jetzt, form);
        await using (var schreiben = postgres.NeuerKontext())
        {
            await new EfRezeptRepository(schreiben).HinzufuegenAsync(rezept, _ct);
        }

        await using var lesen = postgres.NeuerKontext();
        return (await new EfRezeptRepository(lesen).LadenAsync(rezept.Id, _ct))!;
    }

    [Theory]
    [MemberData(nameof(Formen))]
    public async Task Bezugsform_bleibt_beim_Speichern_erhalten(string bezeichnung, Bezugsform? form)
    {
        var geladen = await SpeichernUndLadenAsync(form);

        geladen.MassgeblicheFassung.Bezugsform.ShouldBe(form, bezeichnung);
    }

    public static TheoryData<string, Bezugsform?> Formen => new()
    {
        { "Ring", Bezugsform.Ring(26, 6) },
        { "Blech mit Millimetern", Bezugsform.Blech(60, 40, 1.5m) },
        { "Grenzwerte", Bezugsform.Blech(200, 1, 0.1m) },
        { "ohne Form", null },
    };

    [Fact]
    public async Task Gespeichertes_Format_der_Bezugsform_ist_stabil()
    {
        var geladen = await SpeichernUndLadenAsync(Bezugsform.Ring(26, 6));

        await using var kontext = postgres.NeuerKontext();
        var json = await kontext.Database
            .SqlQuery<string>($"SELECT (fassungen->0->'bezugsform')::text AS \"Value\" FROM rezeptur.rezepte WHERE id = {geladen.Id.Wert}")
            .SingleAsync(_ct);

        using var dokument = System.Text.Json.JsonDocument.Parse(json);
        dokument.RootElement.GetProperty("art").GetString().ShouldBe("Ring");
        dokument.RootElement.GetProperty("durchmesserCm").GetDecimal().ShouldBe(26m);
        dokument.RootElement.GetProperty("hoeheCm").GetDecimal().ShouldBe(6m);
    }

    [Fact]
    public async Task Fassungen_aus_der_Zeit_vor_der_Bezugsform_haben_keine_Form()
    {
        var rezept = await SpeichernUndLadenAsync(Bezugsform.Ring(26, 6));
        await using (var kontext = postgres.NeuerKontext())
        {
            // Stand vor diesem Feature: Fassung ohne Schlüssel "bezugsform".
            await kontext.Database.ExecuteSqlAsync(
                $"UPDATE rezeptur.rezepte SET fassungen = jsonb_set(fassungen, '{{0}}', (fassungen->0) - 'bezugsform') WHERE id = {rezept.Id.Wert}",
                _ct);
        }

        await using var lesen = postgres.NeuerKontext();
        var alt = (await new EfRezeptRepository(lesen).LadenAsync(rezept.Id, _ct))!;

        alt.MassgeblicheFassung.Bezugsform.ShouldBeNull();
        alt.Zutaten.ShouldHaveSingleItem();
    }
}
