using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LightJSC.Infrastructure.Data.Configurations;

public sealed class SubscriberConfiguration : IEntityTypeConfiguration<Subscriber>
{
    public void Configure(EntityTypeBuilder<Subscriber> builder)
    {
        builder.ToTable("subscribers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(128);
        builder.Property(x => x.EndpointUrl).HasColumnName("endpoint_url").HasMaxLength(512);
        builder.Property(x => x.Enabled).HasColumnName("enabled");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}

