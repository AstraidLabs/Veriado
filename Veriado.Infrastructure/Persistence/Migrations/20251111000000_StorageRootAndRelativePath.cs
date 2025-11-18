using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class StorageRootAndRelativePath : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "relative_path",
            table: "filesystem_entities",
            type: "TEXT",
            maxLength: 1024,
            nullable: false,
            defaultValue: string.Empty);

        // TODO: derive relative_path from the configured storage root; for now copy legacy absolute paths for compatibility.
        migrationBuilder.Sql(
            "UPDATE \"filesystem_entities\" SET \"relative_path\" = COALESCE(NULLIF(TRIM(path), ''), '');");

        migrationBuilder.CreateIndex(
            name: "ux_filesystem_entities_relative_path",
            table: "filesystem_entities",
            column: "relative_path",
            unique: true);

        migrationBuilder.CreateTable(
            name: "storage_root",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                root_path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_storage_root", x => x.id);
            });

        // TODO: seed initial storage root during bootstrap/initialisation (Id = 1).
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "storage_root");

        migrationBuilder.DropIndex(
            name: "ux_filesystem_entities_relative_path",
            table: "filesystem_entities");

        migrationBuilder.DropColumn(
            name: "relative_path",
            table: "filesystem_entities");
    }
}
