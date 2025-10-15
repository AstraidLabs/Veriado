using Microsoft.EntityFrameworkCore.Migrations;

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class UpdateDocumentContentSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_ai;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_au;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_ad;");

        migrationBuilder.Sql("ALTER TABLE DocumentContent RENAME TO DocumentContent_old;");

        migrationBuilder.Sql(
            @"CREATE TABLE DocumentContent (
    doc_id INTEGER PRIMARY KEY,
    file_id BLOB NOT NULL UNIQUE,
    title TEXT NULL,
    author TEXT NULL,
    mime TEXT NOT NULL,
    metadata_text TEXT NULL,
    metadata TEXT NULL
);");

        migrationBuilder.Sql(
            @"INSERT INTO DocumentContent (doc_id, file_id, title, author, mime, metadata_text, metadata)
SELECT DocId, FileId, Title, Author, Mime, NULL, NULL
FROM DocumentContent_old;");

        migrationBuilder.Sql("DROP TABLE DocumentContent_old;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER dc_ai AFTER INSERT ON DocumentContent BEGIN
  INSERT INTO file_search(rowid, title, author, mime, metadata_text, metadata)
  VALUES (new.doc_id, new.title, new.author, new.mime, new.metadata_text, new.metadata);
END;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER dc_au AFTER UPDATE ON DocumentContent BEGIN
  INSERT INTO file_search(file_search, rowid)
  VALUES('delete', old.doc_id);
  INSERT INTO file_search(rowid, title, author, mime, metadata_text, metadata)
  VALUES(new.doc_id, new.title, new.author, new.mime, new.metadata_text, new.metadata);
END;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER dc_ad AFTER DELETE ON DocumentContent BEGIN
  INSERT INTO file_search(file_search, rowid)
  VALUES('delete', old.doc_id);
END;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_ai;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_au;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_ad;");

        migrationBuilder.Sql("ALTER TABLE DocumentContent RENAME TO DocumentContent_new;");

        migrationBuilder.Sql(
            @"CREATE TABLE DocumentContent (
    DocId INTEGER PRIMARY KEY,
    FileId BLOB NOT NULL UNIQUE,
    Title TEXT NULL,
    Author TEXT NULL,
    Mime TEXT NOT NULL,
    MetadataText TEXT NULL,
    Metadata TEXT NULL
);");

        migrationBuilder.Sql(
            @"INSERT INTO DocumentContent (DocId, FileId, Title, Author, Mime, MetadataText, Metadata)
SELECT doc_id, file_id, title, author, COALESCE(mime, ''), metadata_text, metadata
FROM DocumentContent_new;");

        migrationBuilder.Sql("DROP TABLE DocumentContent_new;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER dc_ai AFTER INSERT ON DocumentContent BEGIN
  INSERT INTO file_search(rowid, title, author, mime, metadata_text, metadata)
  VALUES (new.DocId, new.Title, new.Author, new.Mime, new.MetadataText, new.Metadata);
END;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER dc_au AFTER UPDATE ON DocumentContent BEGIN
  INSERT INTO file_search(file_search, rowid)
  VALUES('delete', old.DocId);
  INSERT INTO file_search(rowid, title, author, mime, metadata_text, metadata)
  VALUES(new.DocId, new.Title, new.Author, new.Mime, new.MetadataText, new.Metadata);
END;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER dc_ad AFTER DELETE ON DocumentContent BEGIN
  INSERT INTO file_search(file_search, rowid)
  VALUES('delete', old.DocId);
END;");
    }
}
