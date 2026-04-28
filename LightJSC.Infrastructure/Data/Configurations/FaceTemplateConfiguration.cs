using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LightJSC.Infrastructure.Data.Configurations;

public sealed class FaceTemplateConfiguration : IEntityTypeConfiguration<FaceTemplate>
{
    public void Configure(EntityTypeBuilder<FaceTemplate> builder)
    {
        builder.ToTable("face_templates");
        builder.HasKey(template => template.Id);

        builder.Property(template => template.FeatureBytes).IsRequired();
        builder.Property(template => template.FeatureVersion).HasMaxLength(50);
        builder.Property(template => template.SourceCameraId).HasMaxLength(100);
        builder.Property(template => template.FeatureHash).HasMaxLength(128);

        builder.HasIndex(template => template.PersonId);
        builder.HasIndex(template => template.FeatureHash);
        builder.HasIndex(template => template.IsActive);
        builder.HasIndex(template => template.UpdatedAt);
    }
}

