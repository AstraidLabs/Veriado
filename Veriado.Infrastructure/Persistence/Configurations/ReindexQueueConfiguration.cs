using Veriado.Infrastructure.Persistence.EventLog;

namespace Veriado.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the EF Core mapping for <see cref="ReindexQueueEntry"/>.
/// </summary>
internal sealed class ReindexQueueConfiguration : IEntityTypeConfiguration<ReindexQueueEntry>
{
    public void Configure(EntityTypeBuilder<ReindexQueueEntry> builder)
    {
        builder.ToTable("reindex_queue");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasColumnName("id")
            .HasColumnType("INTEGER")
            .ValueGeneratedOnAdd();

        builder.Property(entry => entry.FileId)
            .HasColumnName("file_id")
            .HasColumnType("BLOB")
            .HasConversion(Converters.GuidToBlob)
            .IsRequired();

        builder.Property(entry => entry.Reason)
            .HasColumnName("reason")
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(entry => entry.EnqueuedUtc)
            .HasColumnName("enqueued_utc")
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
            .HasDatabaseName("idx_reindex_queue_unprocessed");
    }
}
