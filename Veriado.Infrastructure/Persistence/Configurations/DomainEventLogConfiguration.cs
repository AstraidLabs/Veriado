using Veriado.Infrastructure.Persistence.EventLog;

namespace Veriado.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the EF Core mapping for <see cref="DomainEventLogEntry"/>.
/// </summary>
internal sealed class DomainEventLogConfiguration : IEntityTypeConfiguration<DomainEventLogEntry>
{
    public void Configure(EntityTypeBuilder<DomainEventLogEntry> builder)
    {
        builder.ToTable("domain_event_log");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasColumnName("id")
            .HasColumnType("INTEGER")
            .ValueGeneratedOnAdd();

        builder.Property(entry => entry.EventType)
            .HasColumnName("event_type")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(entry => entry.EventJson)
            .HasColumnName("event_json")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(entry => entry.AggregateId)
            .HasColumnName("aggregate_id")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(entry => entry.OccurredUtc)
            .HasColumnName("occurred_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.DateTimeOffsetToString)
            .IsRequired();

        builder.Property(entry => entry.ProcessedUtc)
            .HasColumnName("processed_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.NullableDateTimeOffsetToString);

        builder.Property(entry => entry.RetryCount)
            .HasColumnName("retry_count")
            .HasColumnType("INTEGER")
            .HasDefaultValue(0)
            .IsRequired();

        builder.HasIndex(entry => entry.ProcessedUtc)
            .HasDatabaseName("idx_domain_event_log_processed");
    }
}
