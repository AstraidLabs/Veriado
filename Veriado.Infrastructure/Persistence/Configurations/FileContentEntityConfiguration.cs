using System;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Veriado.Domain.Files;

namespace Veriado.Infrastructure.Persistence.Configurations;

/// <summary>
/// Provides configuration for the owned <see cref="FileContentEntity"/> type.
/// </summary>
internal static class FileContentEntityConfiguration
{
    public static void Configure(OwnedNavigationBuilder<FileEntity, FileContentEntity> owned)
    {
        owned.ToTable("files_content");
        owned.WithOwner().HasForeignKey("file_id");
        owned.HasKey("file_id");

        owned.Property<Guid>("file_id")
            .HasColumnName("file_id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .ValueGeneratedNever();

        owned.Property(content => content.Bytes)
            .HasColumnName("bytes")
            .HasColumnType("BLOB")
            .IsRequired();

        owned.Property(content => content.Hash)
            .HasColumnName("hash")
            .HasMaxLength(64)
            .HasConversion(Converters.FileHashToString)
            .IsRequired();

        owned.Ignore(content => content.Length);

        owned.HasIndex(content => content.Hash)
            .IsUnique()
            .HasDatabaseName("ux_files_content_hash");
    }
}
