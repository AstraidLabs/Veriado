namespace Veriado.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the mapping for <see cref="FileEntity"/> and its owned types.
/// </summary>
internal sealed class FileEntityConfiguration : IEntityTypeConfiguration<FileEntity>
{
    public void Configure(EntityTypeBuilder<FileEntity> builder)
    {
        var options = InfrastructureModel.Current;

        builder.ToTable("files");
        builder.HasKey(file => file.Id);
        builder.Property(file => file.Id)
            .HasColumnName("id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .ValueGeneratedNever();

        builder.Property(file => file.Name)
            .HasColumnName("name")
            .HasMaxLength(255)
            .HasConversion(Converters.FileNameToString)
            .IsRequired();

        builder.Property(file => file.Extension)
            .HasColumnName("extension")
            .HasMaxLength(16)
            .HasConversion(Converters.FileExtensionToString)
            .IsRequired();

        builder.Property(file => file.Mime)
            .HasColumnName("mime")
            .HasMaxLength(255)
            .HasConversion(Converters.MimeTypeToString)
            .IsRequired();

        builder.Property(file => file.Author)
            .HasColumnName("author")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(file => file.Title)
            .HasColumnName("title")
            .HasMaxLength(300);

        builder.Property(file => file.Size)
            .HasColumnName("size_bytes")
            .HasConversion(Converters.ByteSizeToLong)
            .IsRequired();

        builder.Property(file => file.CreatedUtc)
            .HasColumnName("created_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.UtcTimestampToString)
            .IsRequired();

        builder.Property(file => file.LastModifiedUtc)
            .HasColumnName("modified_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.UtcTimestampToString)
            .IsRequired();

        builder.Property(file => file.Version)
            .HasColumnName("version")
            .IsRequired();

        builder.Property(file => file.IsReadOnly)
            .HasColumnName("is_read_only")
            .IsRequired();

        builder.Property(file => file.FtsPolicy)
            .HasColumnName("fts_policy")
            .HasColumnType("TEXT")
            .HasConversion(Converters.FtsPolicyToJson)
            .IsRequired();

        builder.Ignore(file => file.DomainEvents);

        builder.HasIndex(file => file.Name).HasDatabaseName("idx_files_name");
        builder.HasIndex(file => file.Mime).HasDatabaseName("idx_files_mime");

        builder.OwnsOne(file => file.Content, FileContentEntityConfiguration.Configure);

        builder.OwnsOne(file => file.Validity, FileDocumentValidityEntityConfiguration.Configure);

        builder.OwnsOne(file => file.SearchIndex, owned =>
        {
            owned.Property(index => index.SchemaVersion)
                .HasColumnName("fts_schema_version")
                .HasDefaultValue(1)
                .IsRequired();

            owned.Property(index => index.IsStale)
                .HasColumnName("fts_is_stale")
                .IsRequired();

            owned.Property(index => index.LastIndexedUtc)
                .HasColumnName("fts_last_indexed_utc")
                .HasColumnType("TEXT")
                .HasConversion(Converters.NullableDateTimeOffsetToString);

            owned.Property(index => index.IndexedContentHash)
                .HasColumnName("fts_indexed_hash")
                .HasMaxLength(64);

            owned.Property(index => index.IndexedTitle)
                .HasColumnName("fts_indexed_title")
                .HasMaxLength(300);

            owned.Property(index => index.AnalyzerVersion)
                .HasColumnName("fts_analyzer_version")
                .HasMaxLength(32)
                .HasDefaultValue(SearchIndexState.DefaultAnalyzerVersion)
                .IsRequired();

            owned.Property(index => index.TokenHash)
                .HasColumnName("fts_token_hash")
                .HasMaxLength(64);
        });

        builder.ComplexProperty(file => file.SystemMetadata, complex =>
        {
            complex.Property(metadata => metadata.Attributes)
                .HasColumnName("fs_attr")
                .HasConversion(Converters.FileAttributesToInt)
                .IsRequired();

            complex.Property(metadata => metadata.CreatedUtc)
                .HasColumnName("fs_created_utc")
                .HasColumnType("TEXT")
                .HasConversion(Converters.UtcTimestampToString)
                .IsRequired();

            complex.Property(metadata => metadata.LastWriteUtc)
                .HasColumnName("fs_write_utc")
                .HasColumnType("TEXT")
                .HasConversion(Converters.UtcTimestampToString)
                .IsRequired();

            complex.Property(metadata => metadata.LastAccessUtc)
                .HasColumnName("fs_access_utc")
                .HasColumnType("TEXT")
                .HasConversion(Converters.UtcTimestampToString)
                .IsRequired();

            complex.Property(metadata => metadata.OwnerSid)
                .HasColumnName("fs_owner_sid")
                .HasMaxLength(256);

            complex.Property(metadata => metadata.HardLinkCount)
                .HasColumnName("fs_links");

            complex.Property(metadata => metadata.AlternateDataStreamCount)
                .HasColumnName("fs_ads");
        });
    }
 }

