using Veriado.Domain.Audit;

namespace Veriado.Infrastructure.Persistence.Configurations;

internal sealed class FileAuditEntityConfiguration : IEntityTypeConfiguration<FileAuditEntity>
{
    public void Configure(EntityTypeBuilder<FileAuditEntity> builder)
    {
        builder.ToTable("audit_file");
        builder.HasKey(audit => new { audit.FileId, audit.OccurredUtc });

        builder.Property(audit => audit.FileId)
            .HasColumnName("file_id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .IsRequired();

        builder.Property(audit => audit.Action)
            .HasColumnName("action")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(audit => audit.Description)
            .HasColumnName("description")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(audit => audit.Mime)
            .HasColumnName("mime")
            .HasMaxLength(255);

        builder.Property(audit => audit.Author)
            .HasColumnName("author")
            .HasMaxLength(256);

        builder.Property(audit => audit.Title)
            .HasColumnName("title")
            .HasMaxLength(300);

        builder.Property(audit => audit.OccurredUtc)
            .HasColumnName("occurred_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.UtcTimestampToString)
            .IsRequired();
    }
}

internal sealed class FileLinkAuditEntityConfiguration : IEntityTypeConfiguration<FileLinkAuditEntity>
{
    public void Configure(EntityTypeBuilder<FileLinkAuditEntity> builder)
    {
        builder.ToTable("audit_file_link");
        builder.HasKey(audit => new { audit.FileId, audit.OccurredUtc });

        builder.Property(audit => audit.FileId)
            .HasColumnName("file_id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .IsRequired();

        builder.Property(audit => audit.FileSystemId)
            .HasColumnName("filesystem_id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .IsRequired();

        builder.Property(audit => audit.Action)
            .HasColumnName("action")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(audit => audit.Version)
            .HasColumnName("version")
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(audit => audit.Hash)
            .HasColumnName("hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(audit => audit.Size)
            .HasColumnName("size")
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(audit => audit.Mime)
            .HasColumnName("mime")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(audit => audit.OccurredUtc)
            .HasColumnName("occurred_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.UtcTimestampToString)
            .IsRequired();
    }
}

internal sealed class FileSystemAuditEntityConfiguration : IEntityTypeConfiguration<FileSystemAuditEntity>
{
    public void Configure(EntityTypeBuilder<FileSystemAuditEntity> builder)
    {
        builder.ToTable("audit_filesystem");
        builder.HasKey(audit => new { audit.FileSystemId, audit.OccurredUtc });

        builder.Property(audit => audit.FileSystemId)
            .HasColumnName("filesystem_id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .IsRequired();

        builder.Property(audit => audit.Action)
            .HasColumnName("action")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(audit => audit.Path)
            .HasColumnName("path")
            .HasColumnType("TEXT");

        builder.Property(audit => audit.Hash)
            .HasColumnName("hash")
            .HasMaxLength(64);

        builder.Property(audit => audit.Size)
            .HasColumnName("size")
            .HasColumnType("INTEGER");

        builder.Property(audit => audit.Mime)
            .HasColumnName("mime")
            .HasMaxLength(255);

        builder.Property(audit => audit.Attributes)
            .HasColumnName("attrs")
            .HasColumnType("INTEGER");

        builder.Property(audit => audit.OwnerSid)
            .HasColumnName("owner_sid")
            .HasMaxLength(256);

        builder.Property(audit => audit.IsEncrypted)
            .HasColumnName("is_encrypted")
            .HasColumnType("INTEGER");

        builder.Property(audit => audit.OccurredUtc)
            .HasColumnName("occurred_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.UtcTimestampToString)
            .IsRequired();
    }
}
