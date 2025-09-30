namespace Veriado.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures search-adjacent entities such as synonyms, suggestions and document geolocation.
/// </summary>
internal sealed class SearchExtrasConfiguration :
    IEntityTypeConfiguration<SynonymEntry>,
    IEntityTypeConfiguration<SuggestionEntry>,
    IEntityTypeConfiguration<DocumentLocationEntity>
{
    public void Configure(EntityTypeBuilder<SynonymEntry> builder)
    {
        builder.ToTable("synonyms");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(entry => entry.Language)
            .HasColumnName("lang")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(entry => entry.Term)
            .HasColumnName("term")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(entry => entry.Variant)
            .HasColumnName("variant")
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(entry => new { entry.Language, entry.Term })
            .HasDatabaseName("idx_synonyms_term");
    }

    public void Configure(EntityTypeBuilder<SuggestionEntry> builder)
    {
        builder.ToTable("suggestions");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(entry => entry.Term)
            .HasColumnName("term")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(entry => entry.Weight)
            .HasColumnName("weight")
            .HasColumnType("REAL")
            .HasDefaultValue(1d);

        builder.Property(entry => entry.Language)
            .HasColumnName("lang")
            .HasMaxLength(16)
            .HasDefaultValue("en")
            .IsRequired();

        builder.Property(entry => entry.SourceField)
            .HasColumnName("source_field")
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(entry => new { entry.Language, entry.Term })
            .HasDatabaseName("idx_suggestions_lookup");

        builder.HasIndex(entry => new { entry.Term, entry.Language, entry.SourceField })
            .IsUnique()
            .HasDatabaseName("ux_suggestions_term");
    }

    public void Configure(EntityTypeBuilder<DocumentLocationEntity> builder)
    {
        builder.ToTable("document_locations");
        builder.HasKey(location => location.FileId);

        builder.Property(location => location.FileId)
            .HasColumnName("file_id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .ValueGeneratedNever();

        builder.Property(location => location.Latitude)
            .HasColumnName("lat")
            .HasColumnType("REAL")
            .IsRequired();

        builder.Property(location => location.Longitude)
            .HasColumnName("lon")
            .HasColumnType("REAL")
            .IsRequired();

        builder.HasIndex(location => new { location.Latitude, location.Longitude })
            .HasDatabaseName("idx_document_locations_geo");

        builder.HasOne<FileEntity>()
            .WithOne()
            .HasForeignKey<DocumentLocationEntity>(location => location.FileId)
            .HasConstraintName("FK_document_locations_files_file_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
