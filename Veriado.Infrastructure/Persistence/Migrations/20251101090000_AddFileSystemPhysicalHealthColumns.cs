using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileSystemPhysicalHealthColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "current_file_path",
                table: "filesystem_entities",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "original_file_path",
                table: "filesystem_entities",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "physical_state",
                table: "filesystem_entities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "current_file_path",
                table: "filesystem_entities");

            migrationBuilder.DropColumn(
                name: "original_file_path",
                table: "filesystem_entities");

            migrationBuilder.DropColumn(
                name: "physical_state",
                table: "filesystem_entities");
        }
    }
}
