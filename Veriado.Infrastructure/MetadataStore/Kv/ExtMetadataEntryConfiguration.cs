using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Veriado.Infrastructure.MetadataStore.Kv;
using Veriado.Infrastructure.Persistence.Configurations;

namespace Veriado.Infrastructure.MetadataStore.Kv;

/// <summary>
/// Configures the EF Core mapping for the key/value metadata table.
/// </summary>
internal sealed class ExtMetadataEntryConfiguration : IEntityTypeConfiguration<ExtMetadataEntry>
{
    public void Configure(EntityTypeBuilder<ExtMetadataEntry> builder)
    {
        builder.ToTable("file_ext_metadata");
        builder.HasKey(entry => new { entry.FileId, entry.FormatId, entry.PropertyId });

        builder.Property(entry => entry.FileId)
            .HasColumnName("file_id")
            .HasConversion(Converters.GuidToBlob)
            .HasColumnType("BLOB")
            .IsRequired();

        builder.Property(entry => entry.FormatId)
            .HasColumnName("fmtid")
            .HasConversion(Converters.GuidToBlob)
            .HasColumnType("BLOB")
            .IsRequired();

        builder.Property(entry => entry.PropertyId)
            .HasColumnName("pid")
            .IsRequired();

        builder.Property(entry => entry.Kind)
            .HasColumnName("kind")
            .HasConversion(new EnumToStringConverter<Veriado.Domain.Metadata.MetadataValueKind>())
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(entry => entry.TextValue)
            .HasColumnName("value_text");

        builder.Property(entry => entry.BinaryValue)
            .HasColumnName("value_blob");

        builder.HasIndex(entry => entry.FileId).HasDatabaseName("idx_file_ext_metadata_file");
    }
}
