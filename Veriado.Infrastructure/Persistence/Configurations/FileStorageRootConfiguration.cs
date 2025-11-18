using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Veriado.Infrastructure.Persistence.Entities;

namespace Veriado.Infrastructure.Persistence.Configurations;

internal sealed class FileStorageRootConfiguration : IEntityTypeConfiguration<FileStorageRootEntity>
{
    public void Configure(EntityTypeBuilder<FileStorageRootEntity> builder)
    {
        builder.ToTable("storage_root");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasColumnName("id")
            .HasColumnType("INTEGER")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.RootPath)
            .HasColumnName("root_path")
            .HasColumnType("TEXT")
            .HasMaxLength(2048)
            .IsRequired();
    }
}
