using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LightJSC.Infrastructure.Data.Configurations;

public sealed class MapCameraPositionConfiguration : IEntityTypeConfiguration<MapCameraPosition>
{
    public void Configure(EntityTypeBuilder<MapCameraPosition> builder)
    {
        builder.ToTable("map_camera_positions");
        builder.HasKey(x => new { x.MapId, x.CameraId });
        builder.Property(x => x.MapId).HasColumnName("map_id");
        builder.Property(x => x.CameraId).HasColumnName("camera_id").HasMaxLength(128);
        builder.Property(x => x.Label).HasColumnName("label").HasMaxLength(200);
        builder.Property(x => x.X).HasColumnName("x");
        builder.Property(x => x.Y).HasColumnName("y");
        builder.Property(x => x.AngleDegrees).HasColumnName("angle_degrees");
        builder.Property(x => x.FovDegrees).HasColumnName("fov_degrees");
        builder.Property(x => x.Range).HasColumnName("range_value");
        builder.Property(x => x.IconScale).HasColumnName("icon_scale");
        builder.Property(x => x.Latitude).HasColumnName("latitude");
        builder.Property(x => x.Longitude).HasColumnName("longitude");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => x.MapId);
    }
}
