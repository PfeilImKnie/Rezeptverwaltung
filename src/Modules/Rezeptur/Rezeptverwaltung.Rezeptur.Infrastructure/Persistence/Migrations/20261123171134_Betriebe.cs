using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Betriebe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "betrieb_id",
                schema: "rezeptur",
                table: "rezepte",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "altbestand");

            // Bestehende Rezepte (vor ADR 0015) gehören dem Betrieb „altbestand“; Zuordnung zu einem echten Betrieb
            // per SQL, siehe ADR 0015. Danach keine Voreinstellung mehr: Jedes neue Rezept braucht einen Betrieb.
            migrationBuilder.Sql("ALTER TABLE rezeptur.rezepte ALTER COLUMN betrieb_id DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "IX_rezepte_betrieb_id",
                schema: "rezeptur",
                table: "rezepte",
                column: "betrieb_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_rezepte_betrieb_id",
                schema: "rezeptur",
                table: "rezepte");

            migrationBuilder.DropColumn(
                name: "betrieb_id",
                schema: "rezeptur",
                table: "rezepte");
        }
    }
}
