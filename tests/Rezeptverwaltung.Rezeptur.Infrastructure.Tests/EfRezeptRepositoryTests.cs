using Microsoft.EntityFrameworkCore;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Tests;

/// <summary>
/// Speichern und Laden über getrennte DbContext-Instanzen, damit wirklich aus der Datenbank gelesen wird
/// und nicht aus dem Change Tracker.
/// </summary>
public sealed class EfRezeptRepositoryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<Rezept> SpeichernUndNeuLadenAsync(Rezept rezept)
    {
        await using (var schreiben = postgres.NeuerKontext())
        {
            await new EfRezeptRepository(schreiben).HinzufuegenAsync(rezept, _ct);
        }

        await using var lesen = postgres.NeuerKontext();
        var geladen = await new EfRezeptRepository(lesen).LadenAsync(rezept.Id, _ct);
        geladen.ShouldNotBeNull();
        return geladen;
    }

    [Fact]
    public async Task Rezept_wird_vollstaendig_gespeichert_und_geladen()
    {
        var rezept = Rezept.Anlegen(
            RezeptName.Von("Sachertorte"),
            [
                Zutatenzeile.Von("Butter", Gewicht.Erfasst(140)),
                Zutatenzeile.Von("Zartbitterkuvertüre", Gewicht.Erfasst(130.5m)),
                Zutatenzeile.Von("Eier", Gewicht.Erfasst(280)),
            ]);

        var geladen = await SpeichernUndNeuLadenAsync(rezept);

        geladen.Id.ShouldBe(rezept.Id);
        geladen.Name.ShouldBe(rezept.Name);
        geladen.Zutaten.ShouldBe(rezept.Zutaten);
        geladen.Gesamtgewicht.ShouldBe(Gewicht.Erfasst(550.5m));
    }

    [Fact]
    public async Task Reihenfolge_und_doppelte_Zutaten_bleiben_erhalten()
    {
        // Fachlich wichtig: Zucker für den Teig und Zucker für den Eischnee sind zwei Zeilen an verschiedenen Stellen.
        var zeilen = new[]
        {
            Zutatenzeile.Von("Zucker", Gewicht.Erfasst(50)),
            Zutatenzeile.Von("Eigelb", Gewicht.Erfasst(120)),
            Zutatenzeile.Von("Eiklar", Gewicht.Erfasst(180)),
            Zutatenzeile.Von("Zucker", Gewicht.Erfasst(50)),
            Zutatenzeile.Von("Mehl", Gewicht.Erfasst(100)),
        };

        var geladen = await SpeichernUndNeuLadenAsync(Rezept.Anlegen(RezeptName.Von("Biskuit"), zeilen));

        geladen.Zutaten.ShouldBe(zeilen);
    }

    [Fact]
    public async Task Rezept_ohne_Zutaten_hat_nach_dem_Laden_eine_leere_Liste()
    {
        var geladen = await SpeichernUndNeuLadenAsync(Rezept.Anlegen(RezeptName.Von("Noch leer"), []));

        geladen.Zutaten.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("0.1")]
    [InlineData("999999999.99")]
    [InlineData("1000000000")]
    public async Task Gewichte_bleiben_auf_den_hundertstel_Gramm_genau(string gramm)
    {
        var menge = Gewicht.Erfasst(decimal.Parse(gramm, System.Globalization.CultureInfo.InvariantCulture));

        var geladen = await SpeichernUndNeuLadenAsync(
            Rezept.Anlegen(RezeptName.Von("Genauigkeit"), [Zutatenzeile.Von("Safran", menge)]));

        geladen.Zutaten.ShouldHaveSingleItem().Menge.ShouldBe(menge);
    }

    [Fact]
    public async Task Unicode_in_Namen_und_Bezeichnungen_bleibt_erhalten()
    {
        var torte = char.ConvertFromUtf32(0x1F382);
        var rezept = Rezept.Anlegen(
            RezeptName.Von($"Crème brûlée {torte} – „Spezial“"),
            [Zutatenzeile.Von($"Bourbon-Vanille (Ø 2 mm) {torte}", Gewicht.Erfasst(2.5m))]);

        var geladen = await SpeichernUndNeuLadenAsync(rezept);

        geladen.Name.ShouldBe(rezept.Name);
        geladen.Zutaten.ShouldBe(rezept.Zutaten);
    }

    [Fact]
    public async Task Unbekanntes_Rezept_ergibt_null()
    {
        await using var kontext = postgres.NeuerKontext();

        (await new EfRezeptRepository(kontext).LadenAsync(RezeptId.Neu(), _ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Dieselbe_Id_kann_nicht_zweimal_gespeichert_werden()
    {
        var rezept = Rezept.Anlegen(RezeptName.Von("Einmalig"), []);
        await SpeichernUndNeuLadenAsync(rezept);

        await using var kontext = postgres.NeuerKontext();
        await Should.ThrowAsync<DbUpdateException>(() => new EfRezeptRepository(kontext).HinzufuegenAsync(rezept, _ct));
    }

    [Fact]
    public async Task Zutaten_liegen_als_JSON_Array_mit_stabilen_Feldnamen_in_der_Datenbank()
    {
        // Das Speicherformat ist ein Vertrag mit künftigen Migrationen: Feldnamen nicht stillschweigend ändern.
        var rezept = Rezept.Anlegen(RezeptName.Von("Format"), [Zutatenzeile.Von("Mehl", Gewicht.Erfasst(100.25m))]);
        await SpeichernUndNeuLadenAsync(rezept);

        await using var kontext = postgres.NeuerKontext();
        var json = await kontext.Database
            .SqlQuery<string>($"SELECT jsonb_build_object('typ', jsonb_typeof(zutaten), 'erste', zutaten->0)::text AS \"Value\" FROM rezeptur.rezepte WHERE id = {rezept.Id.Wert}")
            .SingleAsync(_ct);

        using var dokument = System.Text.Json.JsonDocument.Parse(json);
        dokument.RootElement.GetProperty("typ").GetString().ShouldBe("array");
        var erste = dokument.RootElement.GetProperty("erste");
        erste.GetProperty("bezeichnung").GetString().ShouldBe("Mehl");
        erste.GetProperty("gramm").GetDecimal().ShouldBe(100.25m);
    }
}
