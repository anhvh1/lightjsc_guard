using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LightJSC.Infrastructure.Data.Configurations;

public sealed class DlqMessageConfiguration : IEntityTypeConfiguration<DlqMessage>
{
    public void Configure(EntityTypeBuilder<DlqMessage> builder)
    {
        builder.ToTable("dlq");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.SubscriberId).HasColumnName("subscriber_id");
        builder.Property(x => x.EndpointUrl).HasColumnName("endpoint_url").HasMaxLength(512);
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(256);
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json");
        builder.Property(x => x.Error).HasColumnName("error");
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}

