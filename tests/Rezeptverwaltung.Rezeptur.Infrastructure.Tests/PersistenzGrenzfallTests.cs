using CsCheck;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Tests;

/// <summary>
/// Unabhängiger Test (Akzeptanzkriterium 1): Name, Zutaten mit Reihenfolge, doppelte Zutaten und Gewichte
/// auf 0,01 g genau bleiben über Speichern und Laden vollständig erhalten. Grenzwerte und Eigenschaften.
/// </summary>
public sealed class PersistenzGrenzfallTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
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

    /// <summary>Gewichte über den ganzen erfassbaren Bereich: 0,01 g bis 1.000 t, auf 0,01 g genau.</summary>
    private static readonly Gen<Gewicht> ErfasstesGewicht =
        Gen.Long[1, 100_000_000_000].Select(hundertstel => Gewicht.Erfasst(hundertstel / 100m));

    /// <summary>Freitext mit beliebigen BMP-Zeichen außer U+0000 und Surrogates, beginnt mit einem sichtbaren Zeichen.</summary>
    private static readonly Gen<string> Freitext =
        Gen.String[Gen.Char['\u0001', '퟿'], 0, 150].Select(rest => "R" + rest);

    private static readonly Gen<Zutatenzeile> Zeile =
        Gen.Select(Freitext, ErfasstesGewicht, Zutatenzeile.Von);

    private static readonly Gen<Rezept> BeliebigesRezept =
        Gen.Select(Freitext, Zeile.List[0, 30], (name, zeilen) => Rezept.Anlegen(Testbetrieb.A, RezeptName.Von(name), zeilen, Testzeit.Jetzt));

    [Fact]
    public async Task Jedes_gueltige_Rezept_ist_nach_Speichern_und_Laden_unveraendert()
    {
        await BeliebigesRezept.SampleAsync(
            async rezept =>
            {
                var geladen = await SpeichernUndNeuLadenAsync(rezept);

                geladen.Id.ShouldBe(rezept.Id);
                geladen.Name.ShouldBe(rezept.Name);
                geladen.Zutaten.ShouldBe(rezept.Zutaten);
                geladen.Gesamtgewicht.ShouldBe(rezept.Gesamtgewicht);
            },
            iter: 100,
            threads: 1);
    }

    [Fact]
    public async Task Name_und_Bezeichnung_mit_genau_200_Codepoints_bleiben_erhalten()
    {
        // 200 Emoji = 200 Codepoints, aber 400 UTF-16-Einheiten und 800 Byte UTF-8.
        var torten = string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x1F382), 200));
        var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von(torten), [Zutatenzeile.Von(torten, Gewicht.Erfasst(1))], Testzeit.Jetzt);

        var geladen = await SpeichernUndNeuLadenAsync(rezept);

        geladen.Name.Wert.ShouldBe(torten);
        geladen.Zutaten.ShouldHaveSingleItem().Bezeichnung.ShouldBe(torten);
    }

    [Fact]
    public async Task Grosse_Zutatenliste_behaelt_die_Reihenfolge()
    {
        // Mehr als 10 Zeilen: fällt auf, wenn nach Text sortiert wird ("10" vor "2").
        // Seit Issue #6 gilt die Obergrenze von 100 Zeilen je Fassung; geprüft wird die Reihenfolge an dieser Grenze.
        var zeilen = Enumerable.Range(1, Fassung.MaximaleZutatenzeilen)
            .Select(i => Zutatenzeile.Von($"Zutat {i}", Gewicht.Erfasst(i / 100m)))
            .ToArray();

        var geladen = await SpeichernUndNeuLadenAsync(Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Petits Fours Sortiment"), zeilen, Testzeit.Jetzt));

        geladen.Zutaten.ShouldBe(zeilen);
    }

    [Fact]
    public async Task Mehrere_Zutaten_mit_Hoechstgewicht_ergeben_ein_exaktes_Gesamtgewicht()
    {
        // 5 × 1.000 t: das Gesamtgewicht liegt über dem erfassbaren Maximum und muss trotzdem exakt bleiben.
        var zeilen = Enumerable.Range(0, 5)
            .Select(_ => Zutatenzeile.Von("Mehl", Gewicht.Erfasst(Gewicht.MaximalErfassbarGramm)))
            .Append(Zutatenzeile.Von("Salz", Gewicht.Erfasst(0.01m)))
            .ToArray();

        var geladen = await SpeichernUndNeuLadenAsync(Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Großbäckerei"), zeilen, Testzeit.Jetzt));

        geladen.Gesamtgewicht.Gramm.ShouldBe(5_000_000_000.01m);
    }

    [Fact]
    public async Task Doppelte_identische_Zeilen_bleiben_alle_erhalten()
    {
        // Zweimal exakt gleiche Zeile (gleiche Bezeichnung, gleiches Gewicht) darf nicht zusammenfallen.
        var zucker = Zutatenzeile.Von("Zucker", Gewicht.Erfasst(50));

        var geladen = await SpeichernUndNeuLadenAsync(Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Baiser"), [zucker, zucker, zucker], Testzeit.Jetzt));

        geladen.Zutaten.Count.ShouldBe(3);
        geladen.Zutaten.ShouldAllBe(z => z == zucker);
    }

    [Theory]
    [InlineData("Torte\u0000Spezial")]
    [InlineData("\u0000Torte")]
    public void Nullzeichen_im_Namen_erreicht_die_Datenbank_nie(string name) =>
        // PostgreSQL text/jsonb können U+0000 nicht speichern (gefunden vom unabhängigen Test). Seit ADR 0011
        // weist die Domain es ab, bevor ein Rezept entsteht. Ursprüngliche Erwartung: wird gespeichert.
        Should.Throw<Rezeptverwaltung.SharedKernel.DomainRuleViolationException>(() => RezeptName.Von(name))
            .Code.ShouldBe("rezept.name_nullzeichen");

    [Fact]
    public void Nullzeichen_in_der_Zutatenbezeichnung_erreicht_die_Datenbank_nie() =>
        Should.Throw<Rezeptverwaltung.SharedKernel.DomainRuleViolationException>(
                () => Zutatenzeile.Von("Mehl\u0000Typ 405", Gewicht.Erfasst(100)))
            .Code.ShouldBe("zutat.bezeichnung_nullzeichen");

    [Fact]
    public async Task Viele_gleichzeitig_gespeicherte_Rezepte_sind_alle_ladbar()
    {
        var rezepte = Enumerable.Range(1, 20)
            .Select(i => Rezept.Anlegen(Testbetrieb.A, RezeptName.Von($"Torte {i}"), [Zutatenzeile.Von("Ei", Gewicht.Erfasst(i))], Testzeit.Jetzt))
            .ToArray();

        await Task.WhenAll(rezepte.Select(async rezept =>
        {
            await using var kontext = postgres.NeuerKontext();
            await new EfRezeptRepository(kontext).HinzufuegenAsync(rezept, _ct);
        }));

        foreach (var rezept in rezepte)
        {
            await using var kontext = postgres.NeuerKontext();
            var geladen = await new EfRezeptRepository(kontext).LadenAsync(rezept.Id, _ct);
            geladen.ShouldNotBeNull().Zutaten.ShouldBe(rezept.Zutaten);
        }
    }
}
