using Microsoft.EntityFrameworkCore.Migrations;

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class UnifyRowVersioning : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte[]>(
            name: "row_version",
            table: "filesystem_entities",
            type: "BLOB",
            nullable: false,
            defaultValue: new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });

        migrationBuilder.AddColumn<byte[]>(
            name: "row_version",
            table: "files",
            type: "BLOB",
            nullable: false,
            defaultValue: new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });

        migrationBuilder.RenameColumn(
            name: "version",
            table: "files",
            newName: "content_revision");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "content_revision",
            table: "files",
            newName: "version");

        migrationBuilder.DropColumn(
            name: "row_version",
            table: "files");

        migrationBuilder.DropColumn(
            name: "row_version",
            table: "filesystem_entities");
    }
}
