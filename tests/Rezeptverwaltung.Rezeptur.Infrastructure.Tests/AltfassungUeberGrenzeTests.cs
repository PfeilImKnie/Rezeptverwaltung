using Microsoft.EntityFrameworkCore;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Tests;

/// <summary>
/// Review-Befund (ADR 0015): Eine gespeicherte Fassung aus der Zeit vor der Obergrenze kann über 100 Zeilen haben.
/// Sie bleibt ladbar; ein neuer Entwurf daraus ist ein Zustandskonflikt (409), keine ungültige Eingabe (422).
/// </summary>
public sealed class AltfassungUeberGrenzeTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Fassung_mit_101_gespeicherten_Zeilen_bleibt_ladbar_und_neuer_Entwurf_ist_ein_Zustandskonflikt()
    {
        var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Riesenrezept"), [Zutatenzeile.Von("Mehl", Gewicht.Erfasst(1))], Testzeit.Jetzt);
        rezept.EntwurfFreigeben(Testzeit.Jetzt);
        await using (var schreiben = postgres.NeuerKontext())
        {
            await new EfRezeptRepository(schreiben).HinzufuegenAsync(rezept, _ct);
            var zeilen = string.Join(",", Enumerable.Range(1, 101).Select(i => $$"""{"bezeichnung":"Zutat {{i}}","gramm":1}"""));
            var json = $"[{zeilen}]";
            await schreiben.Database.ExecuteSqlAsync(
                $"UPDATE rezeptur.rezepte SET fassungen = jsonb_set(fassungen, '{{0,zutaten}}', CAST({json} AS jsonb)) WHERE id = {rezept.Id.Wert}",
                _ct);
        }

        await using var lesen = postgres.NeuerKontext();
        var alt = (await new EfRezeptRepository(lesen).LadenAsync(rezept.Id, _ct))!;

        alt.Zutaten.Count.ShouldBe(101);
        var verletzung = Should.Throw<DomainRuleViolationException>(() => alt.EntwurfAnlegen(Testzeit.Jetzt));
        verletzung.Code.ShouldBe("fassung.zu_viele_zutaten");
        verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
    }
}
