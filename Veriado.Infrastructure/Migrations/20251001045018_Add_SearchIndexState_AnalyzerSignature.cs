using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_SearchIndexState_AnalyzerSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fts_analyzer_version",
                table: "files",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "v1");

            migrationBuilder.AddColumn<string>(
                name: "fts_token_hash",
                table: "files",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fts_analyzer_version",
                table: "files");

            migrationBuilder.DropColumn(
                name: "fts_token_hash",
                table: "files");
        }
    }
}
