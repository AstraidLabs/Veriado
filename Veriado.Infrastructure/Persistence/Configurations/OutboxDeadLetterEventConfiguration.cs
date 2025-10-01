namespace Veriado.Infrastructure.Persistence.Configurations;

internal sealed class OutboxDeadLetterEventConfiguration : IEntityTypeConfiguration<OutboxDeadLetterEvent>
{
    public void Configure(EntityTypeBuilder<OutboxDeadLetterEvent> builder)
    {
        builder.ToTable("outbox_dlq");
        builder.HasKey(evt => evt.Id);

        builder.Property(evt => evt.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(evt => evt.OutboxId)
            .HasColumnName("outbox_id")
            .HasColumnType("INTEGER")
            .IsRequired();

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

        builder.Property(evt => evt.DeadLetteredUtc)
            .HasColumnName("dead_lettered_utc")
            .HasColumnType("TEXT")
            .HasConversion(Converters.DateTimeOffsetToString)
            .IsRequired();

        builder.Property(evt => evt.Attempts)
            .HasColumnName("attempts")
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(evt => evt.Error)
            .HasColumnName("error")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.HasIndex(evt => evt.DeadLetteredUtc)
            .HasDatabaseName("idx_outbox_dlq_dead_lettered");
    }
}
