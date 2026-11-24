using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Tests;

/// <summary>Zwei Geräte (z. B. Tablet und Alexa) ändern dasselbe Rezept gleichzeitig: Keine Änderung geht still verloren.</summary>
public sealed class NebenlaeufigkeitTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Zweite_gleichzeitige_Aenderung_wird_mit_Zustandskonflikt_abgewiesen()
    {
        var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Ganache"), [Zutatenzutat("Kuvertüre", 200)], Testzeit.Jetzt);
        await using (var anlegen = postgres.NeuerKontext())
        {
            await new EfRezeptRepository(anlegen).HinzufuegenAsync(rezept, _ct);
        }

        await using var tablet = postgres.NeuerKontext();
        await using var alexa = postgres.NeuerKontext();
        var ausTablet = (await new EfRezeptRepository(tablet).LadenAsync(rezept.Id, _ct))!;
        var ausAlexa = (await new EfRezeptRepository(alexa).LadenAsync(rezept.Id, _ct))!;

        ausTablet.EntwurfBearbeiten([Zutatenzutat("Kuvertüre", 210)]);
        await new EfRezeptRepository(tablet).SpeichernAsync(ausTablet, _ct);

        ausAlexa.EntwurfBearbeiten([Zutatenzutat("Kuvertüre", 190)]);
        var verletzung = await Should.ThrowAsync<DomainRuleViolationException>(() => new EfRezeptRepository(alexa).SpeichernAsync(ausAlexa, _ct));

        verletzung.Code.ShouldBe("rezept.gleichzeitig_geaendert");
        verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
        await using var lesen = postgres.NeuerKontext();
        (await new EfRezeptRepository(lesen).LadenAsync(rezept.Id, _ct))!.Zutaten.ShouldHaveSingleItem().Menge.ShouldBe(Gewicht.Erfasst(210));
    }

    [Fact]
    public async Task Aenderungen_an_Fassungen_werden_dauerhaft_gespeichert()
    {
        var rezept = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("Mürbeteig"), [Zutatenzutat("Mehl", 300)], Testzeit.Jetzt);
        await using (var schreiben = postgres.NeuerKontext())
        {
            var repository = new EfRezeptRepository(schreiben);
            await repository.HinzufuegenAsync(rezept, _ct);
            rezept.EntwurfFreigeben(Testzeit.Jetzt.AddHours(1));
            await repository.SpeichernAsync(rezept, _ct);
            rezept.EntwurfAnlegen(Testzeit.Jetzt.AddHours(2));
            rezept.EntwurfBearbeiten([Zutatenzutat("Mehl", 280), Zutatenzutat("Butter", 200)]);
            await repository.SpeichernAsync(rezept, _ct);
            rezept.EntwurfVerwerfen();
            await repository.SpeichernAsync(rezept, _ct);
            rezept.EntwurfAnlegen(Testzeit.Jetzt.AddHours(3));
            await repository.SpeichernAsync(rezept, _ct);
        }

        await using var lesen = postgres.NeuerKontext();
        var geladen = (await new EfRezeptRepository(lesen).LadenAsync(rezept.Id, _ct))!;

        geladen.Fassungen.Select(f => (f.Nummer, f.Status)).ShouldBe([(1, FassungsStatus.Freigegeben), (3, FassungsStatus.Entwurf)]);
        geladen.LetzteFassungsnummer.ShouldBe(3);
        geladen.FassungMitNummer(1)!.FreigegebenAm.ShouldBe(Testzeit.Jetzt.AddHours(1));
        geladen.FassungMitNummer(3)!.ErstelltAm.ShouldBe(Testzeit.Jetzt.AddHours(3));
        geladen.FassungMitNummer(3)!.Zutaten.ShouldBe(geladen.FassungMitNummer(1)!.Zutaten);
    }

    private static Zutatenzeile Zutatenzutat(string bezeichnung, decimal gramm) => Zutatenzeile.Von(bezeichnung, Gewicht.Erfasst(gramm));
}
