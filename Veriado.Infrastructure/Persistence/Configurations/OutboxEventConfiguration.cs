using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Veriado.Infrastructure.Persistence.Outbox;

namespace Veriado.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the EF Core mapping for <see cref="OutboxEventEntity"/>.
/// </summary>
internal sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEventEntity>
{
    public void Configure(EntityTypeBuilder<OutboxEventEntity> builder)
    {
        builder.ToTable("outbox_events");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasColumnName("id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .ValueGeneratedNever();

        builder.Property(entity => entity.Type)
            .HasColumnName("type")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(entity => entity.PayloadJson)
            .HasColumnName("payload_json")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(entity => entity.CreatedUtc)
            .HasColumnName("created_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.DateTimeOffsetToString)
            .IsRequired();

        builder.Property(entity => entity.Attempts)
            .HasColumnName("attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(entity => entity.LastError)
            .HasColumnName("last_error")
            .HasColumnType("TEXT");

        builder.HasIndex(entity => entity.CreatedUtc)
            .HasDatabaseName("idx_outbox_created");

        builder.HasIndex(entity => entity.Attempts)
            .HasDatabaseName("idx_outbox_attempts");
    }
}
