using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fassungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "zutaten",
                schema: "rezeptur",
                table: "rezepte",
                newName: "fassungen");

            migrationBuilder.AddColumn<int>(
                name: "letzte_fassungsnummer",
                schema: "rezeptur",
                table: "rezepte",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Datenmigration (ADR 0013): Die bisherige Zutatenliste wird zu Fassung 1. Bestehende Rezepte waren
            // bereits in Gebrauch und gelten daher als freigegeben, außer sie haben keine Zutaten: Eine Fassung ohne
            // Zutaten darf nicht freigegeben sein, solche Rezepte werden zum Entwurf.
            migrationBuilder.Sql(
                """
                UPDATE rezeptur.rezepte
                SET letzte_fassungsnummer = 1,
                    fassungen = jsonb_build_array(jsonb_build_object(
                        'nummer', 1,
                        'status', CASE WHEN jsonb_array_length(COALESCE(fassungen, '[]'::jsonb)) > 0 THEN 'Freigegeben' ELSE 'Entwurf' END,
                        'erstelltAm', to_jsonb(now()),
                        'freigegebenAm', CASE WHEN jsonb_array_length(COALESCE(fassungen, '[]'::jsonb)) > 0 THEN to_jsonb(now()) ELSE 'null'::jsonb END,
                        'zutaten', COALESCE(fassungen, '[]'::jsonb)));
                """);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "rezeptur",
                table: "rezepte",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "letzte_fassungsnummer",
                schema: "rezeptur",
                table: "rezepte");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "rezeptur",
                table: "rezepte");

            // Rückweg verliert die Fassungshistorie: Es bleibt die Zutatenliste der maßgeblichen Fassung
            // (neueste freigegebene, sonst der Entwurf). Ein nie freigegebener Entwurf wird so nie zum Rezept.
            migrationBuilder.Sql(
                """
                UPDATE rezeptur.rezepte
                SET fassungen = (
                    SELECT f -> 'zutaten'
                    FROM jsonb_array_elements(fassungen) AS f
                    ORDER BY (f ->> 'status') = 'Freigegeben' DESC, (f ->> 'nummer')::int DESC
                    LIMIT 1);
                """);

            migrationBuilder.RenameColumn(
                name: "fassungen",
                schema: "rezeptur",
                table: "rezepte",
                newName: "zutaten");
        }
    }
}
