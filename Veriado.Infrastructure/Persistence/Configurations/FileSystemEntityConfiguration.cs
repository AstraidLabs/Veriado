namespace Veriado.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the persistence mapping for <see cref="FileSystemEntity"/>.
/// </summary>
internal sealed class FileSystemEntityConfiguration : IEntityTypeConfiguration<FileSystemEntity>
{
    public void Configure(EntityTypeBuilder<FileSystemEntity> builder)
    {
        builder.ToTable("filesystem_entities");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasColumnName("id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .ValueGeneratedNever();

        builder.Property(entity => entity.Provider)
            .HasColumnName("provider")
            .HasColumnType("INTEGER")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(entity => entity.Path)
            .HasColumnName("path")
            .HasColumnType("TEXT")
            .HasConversion(Converters.StoragePathToString)
            .IsRequired();

        builder.Property(entity => entity.Hash)
            .HasColumnName("hash")
            .HasMaxLength(64)
            .HasConversion(Converters.FileHashToString)
            .IsRequired();

        builder.Property(entity => entity.Size)
            .HasColumnName("size")
            .HasColumnType("BIGINT")
            .HasConversion(Converters.ByteSizeToLong)
            .IsRequired();

        builder.Property(entity => entity.Mime)
            .HasColumnName("mime")
            .HasMaxLength(255)
            .HasConversion(Converters.MimeTypeToString)
            .IsRequired();

        builder.Property(entity => entity.Attributes)
            .HasColumnName("attributes")
            .HasColumnType("INTEGER")
            .HasConversion(Converters.FileAttributesToInt)
            .IsRequired();

        builder.Property(entity => entity.OwnerSid)
            .HasColumnName("owner_sid")
            .HasMaxLength(256);

        builder.Property(entity => entity.IsEncrypted)
            .HasColumnName("is_encrypted")
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(entity => entity.IsMissing)
            .HasColumnName("is_missing")
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(entity => entity.MissingSinceUtc)
            .HasColumnName("missing_since_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.NullableUtcTimestampToString);

        builder.Property(entity => entity.ContentVersion)
            .HasColumnName("content_version")
            .HasColumnType("INTEGER")
            .HasConversion(Converters.ContentVersionToInt)
            .IsRequired();

        builder.Property(entity => entity.CreatedUtc)
            .HasColumnName("created_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.UtcTimestampToString)
            .IsRequired();

        builder.Property(entity => entity.LastWriteUtc)
            .HasColumnName("last_write_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.UtcTimestampToString)
            .IsRequired();

        builder.Property(entity => entity.LastAccessUtc)
            .HasColumnName("last_access_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.UtcTimestampToString)
            .IsRequired();

        builder.Property(entity => entity.LastLinkedUtc)
            .HasColumnName("last_linked_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.NullableUtcTimestampToString);

        builder.Ignore(entity => entity.DomainEvents);

        builder.HasIndex(entity => entity.Path)
            .HasDatabaseName("ux_filesystem_entities_path")
            .IsUnique();

        builder.HasIndex(entity => entity.Hash)
            .HasDatabaseName("idx_filesystem_entities_hash");
    }
}
