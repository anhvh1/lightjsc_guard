using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LightJSC.Infrastructure.Data.Configurations;

public sealed class RuntimeStateConfiguration : IEntityTypeConfiguration<RuntimeState>
{
    public void Configure(EntityTypeBuilder<RuntimeState> builder)
    {
        builder.ToTable("runtime_state");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(128);
        builder.Property(x => x.Value).HasColumnName("value");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}

