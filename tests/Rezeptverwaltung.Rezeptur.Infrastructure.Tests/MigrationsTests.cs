using Microsoft.EntityFrameworkCore;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Tests;

public sealed class MigrationsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Modell_und_Migrationen_stimmen_ueberein()
    {
        await using var kontext = postgres.NeuerKontext();

        kontext.Database.HasPendingModelChanges().ShouldBeFalse(
            "Modell geändert ohne Migration: dotnet ef migrations add <Name> --project src/Modules/Rezeptur/Rezeptverwaltung.Rezeptur.Infrastructure --startup-project src/Modules/Rezeptur/Rezeptverwaltung.Rezeptur.Infrastructure --output-dir Persistence/Migrations");
    }

    [Fact]
    public async Task Migrationen_laufen_auf_leerer_Datenbank_und_sind_wiederholbar()
    {
        await using var kontext = postgres.NeuerKontext(postgres.NeueDatenbank());

        await kontext.Database.MigrateAsync(_ct);
        await kontext.Database.MigrateAsync(_ct);

        (await kontext.Database.GetPendingMigrationsAsync(_ct)).ShouldBeEmpty();
        (await kontext.Database.GetAppliedMigrationsAsync(_ct)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Tabellen_liegen_im_Schema_des_Moduls()
    {
        await using var kontext = postgres.NeuerKontext();

        var tabellen = await kontext.Database
            .SqlQuery<string>($"SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'rezeptur'")
            .ToListAsync(_ct);

        tabellen.ShouldBe(["__ef_migrationen", "rezepte"], ignoreOrder: true);
    }
}
