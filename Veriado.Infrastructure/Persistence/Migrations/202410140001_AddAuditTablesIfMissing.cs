using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <summary>
/// Ensures the audit tables exist without failing if they have already been created
/// by an earlier version of the application.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("202410140001_AddAuditTablesIfMissing")]
public sealed class AddAuditTablesIfMissing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS "audit_file" (
                "file_id" BLOB NOT NULL,
                "action" TEXT NOT NULL,
                "description" TEXT NOT NULL,
                "occurred_utc" TEXT NOT NULL,
                CONSTRAINT "PK_audit_file" PRIMARY KEY ("file_id", "occurred_utc")
            );
            """
        );

        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS "audit_file_content" (
                "file_id" BLOB NOT NULL,
                "new_hash" TEXT NOT NULL,
                "occurred_utc" TEXT NOT NULL,
                CONSTRAINT "PK_audit_file_content" PRIMARY KEY ("file_id", "occurred_utc")
            );
            """
        );

        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS "audit_file_validity" (
                "file_id" BLOB NOT NULL,
                "issued_at" TEXT NULL,
                "valid_until" TEXT NULL,
                "has_physical" INTEGER NOT NULL,
                "has_electronic" INTEGER NOT NULL,
                "occurred_utc" TEXT NOT NULL,
                CONSTRAINT "PK_audit_file_validity" PRIMARY KEY ("file_id", "occurred_utc")
            );
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql("DROP TABLE IF EXISTS \"audit_file\";");
        migrationBuilder.Sql("DROP TABLE IF EXISTS \"audit_file_content\";");
        migrationBuilder.Sql("DROP TABLE IF EXISTS \"audit_file_validity\";");
    }
}
