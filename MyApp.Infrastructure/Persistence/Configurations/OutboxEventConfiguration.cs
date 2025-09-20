using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Veriado.Infrastructure.Search.Outbox;

namespace Veriado.Infrastructure.Persistence.Configurations;

internal sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_events");
        builder.HasKey(evt => evt.Id);

        builder.Property(evt => evt.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(evt => evt.Type)
            .HasColumnName("type")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(evt => evt.Payload)
            .HasColumnName("payload")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(evt => evt.CreatedUtc)
            .HasColumnName("created_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.DateTimeOffsetToString)
            .IsRequired();

        builder.Property(evt => evt.ProcessedUtc)
            .HasColumnName("processed_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.NullableDateTimeOffsetToString);

        builder.HasIndex(evt => evt.ProcessedUtc).HasDatabaseName("idx_outbox_processed");
    }
}
