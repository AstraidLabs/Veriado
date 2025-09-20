using System;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Veriado.Domain.Files;

namespace Veriado.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the owned document validity entity.
/// </summary>
internal static class FileDocumentValidityEntityConfiguration
{
    public static void Configure(OwnedNavigationBuilder<FileEntity, FileDocumentValidityEntity> owned)
    {
        owned.ToTable("files_validity");
        owned.WithOwner().HasForeignKey("file_id");
        owned.HasKey("file_id");

        owned.Property<Guid>("file_id")
            .HasColumnName("file_id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .ValueGeneratedNever();

        owned.Property(validity => validity.IssuedAt)
            .HasColumnName("issued_at")
            .HasColumnType("TEXT")
            .HasConversion(Converters.UtcTimestampToString);

        owned.Property(validity => validity.ValidUntil)
            .HasColumnName("valid_until")
            .HasColumnType("TEXT")
            .HasConversion(Converters.UtcTimestampToString);

        owned.Property(validity => validity.HasPhysicalCopy)
            .HasColumnName("has_physical")
            .IsRequired();

        owned.Property(validity => validity.HasElectronicCopy)
            .HasColumnName("has_electronic")
            .IsRequired();
    }
}
