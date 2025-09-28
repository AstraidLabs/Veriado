using Veriado.Infrastructure.Persistence.Configurations;

namespace Veriado.Infrastructure.Persistence.Configurations;

internal sealed class SearchHistoryConfiguration : IEntityTypeConfiguration<SearchHistoryEntryEntity>
{
    public void Configure(EntityTypeBuilder<SearchHistoryEntryEntity> builder)
    {
        builder.ToTable("search_history");

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id)
            .HasColumnName("id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .ValueGeneratedNever();

        builder.Property(entry => entry.QueryText)
            .HasColumnName("query_text")
            .HasColumnType("TEXT");

        builder.Property(entry => entry.Match)
            .HasColumnName("match")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(entry => entry.CreatedUtc)
            .HasColumnName("created_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.DateTimeOffsetToString)
            .IsRequired();

        builder.Property(entry => entry.Executions)
            .HasColumnName("executions")
            .HasColumnType("INTEGER")
            .HasDefaultValue(1);

        builder.Property(entry => entry.LastTotalHits)
            .HasColumnName("last_total_hits")
            .HasColumnType("INTEGER");

        builder.Property(entry => entry.IsFuzzy)
            .HasColumnName("is_fuzzy")
            .HasColumnType("INTEGER")
            .HasDefaultValue(false);

        builder.HasIndex(entry => entry.CreatedUtc)
            .HasDatabaseName("idx_search_history_created")
            .IsDescending();

        builder.HasIndex(entry => entry.Match)
            .HasDatabaseName("idx_search_history_match");
    }
}
