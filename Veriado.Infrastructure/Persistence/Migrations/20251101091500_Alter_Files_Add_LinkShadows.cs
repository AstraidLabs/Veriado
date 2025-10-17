using Microsoft.EntityFrameworkCore.Migrations;

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class Alter_Files_Add_LinkShadows : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte[]>(
            name: "filesystem_id",
            table: "files",
            type: "BLOB",
            nullable: true);

        migrationBuilder.Sql("UPDATE files SET filesystem_id = randomblob(16) WHERE filesystem_id IS NULL;");

        migrationBuilder.AddColumn<string>(
            name: "content_hash",
            table: "files",
            type: "TEXT",
            nullable: true);

        migrationBuilder.Sql(
            @"UPDATE files SET content_hash = (
    SELECT hash FROM files_content WHERE file_id = files.id LIMIT 1
);");

        migrationBuilder.Sql(
            "UPDATE files SET content_hash = lower(hex(randomblob(32))) WHERE content_hash IS NULL OR content_hash = '';\n");

        migrationBuilder.AddColumn<int>(
            name: "content_version",
            table: "files",
            type: "INTEGER",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.Sql(
            @"INSERT INTO filesystem_entities (
    id,
    provider,
    path,
    hash,
    size,
    mime,
    attributes,
    owner_sid,
    is_encrypted,
    is_missing,
    missing_since_utc,
    content_version,
    created_utc,
    last_write_utc,
    last_access_utc,
    last_linked_utc
)
SELECT
    filesystem_id,
    0,
    'legacy://' || lower(hex(id)),
    content_hash,
    size_bytes,
    mime,
    fs_attr,
    fs_owner_sid,
    0,
    0,
    NULL,
    content_version,
    fs_created_utc,
    fs_write_utc,
    fs_access_utc,
    created_utc
FROM files
WHERE filesystem_id NOT IN (SELECT id FROM filesystem_entities);");

        migrationBuilder.Sql(
            @"CREATE TABLE files_new (
    id BLOB NOT NULL,
    name TEXT NOT NULL,
    extension TEXT NOT NULL,
    mime TEXT NOT NULL,
    size_bytes BIGINT NOT NULL,
    author TEXT NOT NULL,
    is_read_only INTEGER NOT NULL,
    version INTEGER NOT NULL,
    created_utc TEXT NOT NULL,
    modified_utc TEXT NOT NULL,
    title TEXT NULL,
    filesystem_id BLOB NOT NULL,
    content_hash TEXT NOT NULL,
    content_version INTEGER NOT NULL,
    fts_is_stale INTEGER NOT NULL,
    fts_schema_version INTEGER NOT NULL DEFAULT 1,
    fts_last_indexed_utc TEXT NULL,
    fts_indexed_hash TEXT NULL,
    fts_indexed_title TEXT NULL,
    fts_analyzer_version TEXT NOT NULL DEFAULT 'v1',
    fts_token_hash TEXT NULL,
    fts_policy TEXT NOT NULL,
    fs_ads INTEGER NULL,
    fs_attr INTEGER NOT NULL,
    fs_created_utc TEXT NOT NULL,
    fs_links INTEGER NULL,
    fs_access_utc TEXT NOT NULL,
    fs_write_utc TEXT NOT NULL,
    fs_owner_sid TEXT NULL,
    CONSTRAINT PK_files_new PRIMARY KEY (id),
    CONSTRAINT FK_files_filesystem_entities_filesystem_id FOREIGN KEY (filesystem_id) REFERENCES filesystem_entities (id) ON DELETE RESTRICT
);");

        migrationBuilder.Sql(
            @"INSERT INTO files_new (
    id,
    name,
    extension,
    mime,
    size_bytes,
    author,
    is_read_only,
    version,
    created_utc,
    modified_utc,
    title,
    filesystem_id,
    content_hash,
    content_version,
    fts_is_stale,
    fts_schema_version,
    fts_last_indexed_utc,
    fts_indexed_hash,
    fts_indexed_title,
    fts_analyzer_version,
    fts_token_hash,
    fts_policy,
    fs_ads,
    fs_attr,
    fs_created_utc,
    fs_links,
    fs_access_utc,
    fs_write_utc,
    fs_owner_sid
)
SELECT
    id,
    name,
    extension,
    mime,
    size_bytes,
    author,
    is_read_only,
    version,
    created_utc,
    modified_utc,
    title,
    filesystem_id,
    content_hash,
    content_version,
    fts_is_stale,
    fts_schema_version,
    fts_last_indexed_utc,
    fts_indexed_hash,
    fts_indexed_title,
    fts_analyzer_version,
    fts_token_hash,
    fts_policy,
    fs_ads,
    fs_attr,
    fs_created_utc,
    fs_links,
    fs_access_utc,
    fs_write_utc,
    fs_owner_sid
FROM files;");

        migrationBuilder.Sql("DROP TABLE files;");
        migrationBuilder.Sql("ALTER TABLE files_new RENAME TO files;");

        migrationBuilder.Sql("CREATE INDEX idx_files_name ON files(name);");
        migrationBuilder.Sql("CREATE INDEX idx_files_mime ON files(mime);");
        migrationBuilder.Sql("CREATE UNIQUE INDEX ux_files_filesystem_id ON files(filesystem_id);");

        migrationBuilder.Sql("UPDATE files SET filesystem_id = randomblob(16) WHERE filesystem_id IS NULL;");
        migrationBuilder.Sql("UPDATE files SET content_hash = lower(hex(randomblob(32))) WHERE content_hash IS NULL OR content_hash = '';\n");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS ux_files_filesystem_id;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS idx_files_mime;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS idx_files_name;");

        migrationBuilder.Sql(
            @"CREATE TABLE files_legacy (
    id BLOB NOT NULL,
    name TEXT NOT NULL,
    extension TEXT NOT NULL,
    mime TEXT NOT NULL,
    size_bytes BIGINT NOT NULL,
    author TEXT NOT NULL,
    is_read_only INTEGER NOT NULL,
    version INTEGER NOT NULL,
    created_utc TEXT NOT NULL,
    modified_utc TEXT NOT NULL,
    title TEXT NULL,
    fts_is_stale INTEGER NOT NULL,
    fts_schema_version INTEGER NOT NULL DEFAULT 1,
    fts_last_indexed_utc TEXT NULL,
    fts_indexed_hash TEXT NULL,
    fts_indexed_title TEXT NULL,
    fts_analyzer_version TEXT NOT NULL DEFAULT 'v1',
    fts_token_hash TEXT NULL,
    fts_policy TEXT NOT NULL,
    fs_ads INTEGER NULL,
    fs_attr INTEGER NOT NULL,
    fs_created_utc TEXT NOT NULL,
    fs_links INTEGER NULL,
    fs_access_utc TEXT NOT NULL,
    fs_write_utc TEXT NOT NULL,
    fs_owner_sid TEXT NULL,
    CONSTRAINT PK_files_legacy PRIMARY KEY (id)
);");

        migrationBuilder.Sql(
            @"INSERT INTO files_legacy (
    id,
    name,
    extension,
    mime,
    size_bytes,
    author,
    is_read_only,
    version,
    created_utc,
    modified_utc,
    title,
    fts_is_stale,
    fts_schema_version,
    fts_last_indexed_utc,
    fts_indexed_hash,
    fts_indexed_title,
    fts_analyzer_version,
    fts_token_hash,
    fts_policy,
    fs_ads,
    fs_attr,
    fs_created_utc,
    fs_links,
    fs_access_utc,
    fs_write_utc,
    fs_owner_sid
)
SELECT
    id,
    name,
    extension,
    mime,
    size_bytes,
    author,
    is_read_only,
    version,
    created_utc,
    modified_utc,
    title,
    fts_is_stale,
    fts_schema_version,
    fts_last_indexed_utc,
    fts_indexed_hash,
    fts_indexed_title,
    fts_analyzer_version,
    fts_token_hash,
    fts_policy,
    fs_ads,
    fs_attr,
    fs_created_utc,
    fs_links,
    fs_access_utc,
    fs_write_utc,
    fs_owner_sid
FROM files;");

        migrationBuilder.Sql("DROP TABLE files;");
        migrationBuilder.Sql("ALTER TABLE files_legacy RENAME TO files;");

        migrationBuilder.Sql("CREATE INDEX idx_files_name ON files(name);");
        migrationBuilder.Sql("CREATE INDEX idx_files_mime ON files(mime);");

        migrationBuilder.Sql("DELETE FROM filesystem_entities WHERE path LIKE 'legacy://%';");
    }
}
