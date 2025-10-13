using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Fts5ContentLinked : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_trgm;");

            migrationBuilder.Sql(@"CREATE TABLE IF NOT EXISTS DocumentContent (
    DocId INTEGER PRIMARY KEY,
    FileId BLOB NOT NULL UNIQUE,
    Title TEXT NULL,
    Author TEXT NULL,
    Mime TEXT NOT NULL,
    MetadataText TEXT NULL,
    Metadata TEXT NULL
);");

            migrationBuilder.Sql(@"INSERT INTO DocumentContent (DocId, FileId, Title, Author, Mime, MetadataText, Metadata)
SELECT m.rowid, m.file_id, s.title, s.author, s.mime, s.metadata_text, s.metadata
FROM file_search_map m
JOIN file_search s ON s.rowid = m.rowid;");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_ai;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_ad;");

            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_data;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_idx;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_content;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_docsize;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_config;");

            migrationBuilder.Sql(@"CREATE VIRTUAL TABLE file_search USING fts5(
    title,
    author,
    mime,
    metadata_text,
    metadata,
    tokenize='unicode61 remove_diacritics 2'
);");

            migrationBuilder.Sql(@"CREATE TRIGGER IF NOT EXISTS dc_ai AFTER INSERT ON DocumentContent BEGIN
  INSERT INTO file_search(rowid, title, author, mime, metadata_text, metadata)
  VALUES (new.DocId, new.Title, new.Author, new.Mime, new.MetadataText, new.Metadata);
END;");

            migrationBuilder.Sql(@"CREATE TRIGGER IF NOT EXISTS dc_au AFTER UPDATE ON DocumentContent BEGIN
  INSERT INTO file_search(file_search, rowid)
  VALUES('delete', old.DocId);
  INSERT INTO file_search(rowid, title, author, mime, metadata_text, metadata)
  VALUES(new.DocId, new.Title, new.Author, new.Mime, new.MetadataText, new.Metadata);
END;");

            migrationBuilder.Sql(@"CREATE TRIGGER IF NOT EXISTS dc_ad AFTER DELETE ON DocumentContent BEGIN
  INSERT INTO file_search(file_search, rowid)
  VALUES('delete', old.DocId);
END;");

            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_map;");

            migrationBuilder.Sql("INSERT INTO file_search(file_search) VALUES('rebuild');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_ai;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_ad;");

            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_data;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_idx;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_content;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_docsize;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_config;");

            migrationBuilder.Sql(@"CREATE VIRTUAL TABLE file_search USING fts5(
    title,
    mime,
    author,
    metadata_text,
    metadata,
    tokenize='unicode61 remove_diacritics 2',
    content='',
    columnsize=0
);");

            migrationBuilder.Sql(@"CREATE TABLE IF NOT EXISTS file_search_map (
    rowid INTEGER PRIMARY KEY,
    file_id BLOB NOT NULL UNIQUE
);");

            migrationBuilder.Sql(@"INSERT INTO file_search_map(rowid, file_id)
SELECT DocId, FileId FROM DocumentContent;");

            migrationBuilder.Sql(@"INSERT INTO file_search(rowid, title, mime, author, metadata_text, metadata)
SELECT DocId, Title, Mime, Author, MetadataText, Metadata FROM DocumentContent;");

            migrationBuilder.Sql("DROP TABLE IF EXISTS DocumentContent;");
        }
    }
}
