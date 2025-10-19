using Veriado.Infrastructure.Persistence.Entities;

namespace Veriado.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the persisted history of file content links.
/// </summary>
internal sealed class FileContentLinkConfiguration : IEntityTypeConfiguration<FileContentLinkRow>
{
    public void Configure(EntityTypeBuilder<FileContentLinkRow> builder)
    {
        builder.ToTable("file_content_link");
        builder.HasKey(row => new { row.FileId, row.ContentVersion });

        builder.Property(row => row.FileId)
            .HasColumnName("file_id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .IsRequired();

        builder.Property(row => row.ContentVersion)
            .HasColumnName("content_version")
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(row => row.Provider)
            .HasColumnName("provider")
            .HasColumnType("TEXT")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(row => row.Location)
            .HasColumnName("location")
            .HasColumnType("TEXT")
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(row => row.ContentHash)
            .HasColumnName("content_hash")
            .HasColumnType("TEXT")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(row => row.SizeBytes)
            .HasColumnName("size_bytes")
            .HasColumnType("BIGINT")
            .IsRequired();

        builder.Property(row => row.Mime)
            .HasColumnName("mime")
            .HasColumnType("TEXT")
            .HasMaxLength(255);

        builder.Property(row => row.CreatedUtc)
            .HasColumnName("created_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.DateTimeOffsetToString)
            .IsRequired();

        builder.HasIndex(row => row.ContentHash).HasDatabaseName("idx_file_content_link_hash");

        builder.HasOne(row => row.File)
            .WithMany()
            .HasForeignKey(row => row.FileId)
            .HasPrincipalKey(file => file.Id)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_file_content_link_files_file_id");
    }
}
