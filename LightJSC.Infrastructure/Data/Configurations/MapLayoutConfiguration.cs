using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LightJSC.Infrastructure.Data.Configurations;

public sealed class MapLayoutConfiguration : IEntityTypeConfiguration<MapLayout>
{
    public void Configure(EntityTypeBuilder<MapLayout> builder)
    {
        builder.ToTable("map_layouts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ParentId).HasColumnName("parent_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
        builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(16);
        builder.Property(x => x.ImagePath).HasColumnName("image_path").HasMaxLength(512);
        builder.Property(x => x.ImageWidth).HasColumnName("image_width");
        builder.Property(x => x.ImageHeight).HasColumnName("image_height");
        builder.Property(x => x.GeoCenterLatitude).HasColumnName("geo_center_latitude");
        builder.Property(x => x.GeoCenterLongitude).HasColumnName("geo_center_longitude");
        builder.Property(x => x.GeoZoom).HasColumnName("geo_zoom");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => x.Type);
        builder.HasIndex(x => x.ParentId);
    }
}
