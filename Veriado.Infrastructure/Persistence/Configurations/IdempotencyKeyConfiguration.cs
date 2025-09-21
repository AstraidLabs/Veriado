using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Veriado.Infrastructure.Idempotency.Entities;

namespace Veriado.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the <see cref="IdempotencyKeyEntity"/> mapping.
/// </summary>
internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKeyEntity>
{
    public void Configure(EntityTypeBuilder<IdempotencyKeyEntity> builder)
    {
        builder.ToTable("idempotency_keys");

        builder.HasKey(entry => entry.Key);

        builder.Property(entry => entry.Key)
            .HasColumnName("key")
            .HasColumnType("TEXT")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(entry => entry.CreatedUtc)
            .HasColumnName("created_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.DateTimeOffsetToString)
            .IsRequired();
    }
}
