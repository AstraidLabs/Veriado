using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Veriado.Infrastructure.Search.Entities;

namespace Veriado.Infrastructure.Persistence.Configurations;

internal sealed class SearchFavoriteConfiguration : IEntityTypeConfiguration<SearchFavoriteEntity>
{
    public void Configure(EntityTypeBuilder<SearchFavoriteEntity> builder)
    {
        builder.ToTable("search_favorites");

        builder.HasKey(favorite => favorite.Id);
        builder.Property(favorite => favorite.Id)
            .HasColumnName("id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .ValueGeneratedNever();

        builder.Property(favorite => favorite.Name)
            .HasColumnName("name")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(favorite => favorite.QueryText)
            .HasColumnName("query_text")
            .HasColumnType("TEXT");

        builder.Property(favorite => favorite.Match)
            .HasColumnName("match")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(favorite => favorite.Position)
            .HasColumnName("position")
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(favorite => favorite.CreatedUtc)
            .HasColumnName("created_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.DateTimeOffsetToString)
            .IsRequired();

        builder.Property(favorite => favorite.IsFuzzy)
            .HasColumnName("is_fuzzy")
            .HasColumnType("INTEGER")
            .HasDefaultValue(false);

        builder.HasIndex(favorite => favorite.Position)
            .HasDatabaseName("idx_search_favorites_position");

        builder.HasIndex(favorite => favorite.Name)
            .HasDatabaseName("ux_search_favorites_name")
            .IsUnique();
    }
}
