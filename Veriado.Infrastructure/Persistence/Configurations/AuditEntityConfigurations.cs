using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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

        builder.Property(audit => audit.OccurredUtc)
            .HasColumnName("occurred_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.UtcTimestampToString)
            .IsRequired();
    }
}

internal sealed class FileContentAuditEntityConfiguration : IEntityTypeConfiguration<FileContentAuditEntity>
{
    public void Configure(EntityTypeBuilder<FileContentAuditEntity> builder)
    {
        builder.ToTable("audit_file_content");
        builder.HasKey(audit => new { audit.FileId, audit.OccurredUtc });

        builder.Property(audit => audit.FileId)
            .HasColumnName("file_id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .IsRequired();

        builder.Property(audit => audit.NewHash)
            .HasColumnName("new_hash")
            .HasMaxLength(64)
            .HasConversion(Converters.FileHashToString)
            .IsRequired();

        builder.Property(audit => audit.OccurredUtc)
            .HasColumnName("occurred_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.UtcTimestampToString)
            .IsRequired();
    }
}

internal sealed class FileDocumentValidityAuditEntityConfiguration : IEntityTypeConfiguration<FileDocumentValidityAuditEntity>
{
    public void Configure(EntityTypeBuilder<FileDocumentValidityAuditEntity> builder)
    {
        builder.ToTable("audit_file_validity");
        builder.HasKey(audit => new { audit.FileId, audit.OccurredUtc });

        builder.Property(audit => audit.FileId)
            .HasColumnName("file_id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .IsRequired();

        builder.Property(audit => audit.IssuedAt)
            .HasColumnName("issued_at")
            .HasColumnType("TEXT")
            .HasConversion(Converters.NullableUtcTimestampToString);

        builder.Property(audit => audit.ValidUntil)
            .HasColumnName("valid_until")
            .HasColumnType("TEXT")
            .HasConversion(Converters.NullableUtcTimestampToString);

        builder.Property(audit => audit.HasPhysicalCopy)
            .HasColumnName("has_physical")
            .IsRequired();

        builder.Property(audit => audit.HasElectronicCopy)
            .HasColumnName("has_electronic")
            .IsRequired();

        builder.Property(audit => audit.OccurredUtc)
            .HasColumnName("occurred_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.UtcTimestampToString)
            .IsRequired();
    }
}
