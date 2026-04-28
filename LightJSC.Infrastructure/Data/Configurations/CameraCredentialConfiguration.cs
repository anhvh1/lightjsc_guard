using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LightJSC.Infrastructure.Data.Configurations;

public sealed class CameraCredentialConfiguration : IEntityTypeConfiguration<CameraCredential>
{
    public void Configure(EntityTypeBuilder<CameraCredential> builder)
    {
        builder.ToTable("cameras");
        builder.HasKey(x => x.CameraId);
        builder.Property(x => x.CameraId).HasColumnName("camera_id").HasMaxLength(128);
        builder.Property(x => x.Code).HasColumnName("camera_code").HasMaxLength(128);
        builder.Property(x => x.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
        builder.Property(x => x.RtspUsername).HasColumnName("rtsp_username").HasMaxLength(128);
        builder.Property(x => x.RtspPasswordEncrypted).HasColumnName("rtsp_password_encrypted");
        builder.Property(x => x.RtspProfile).HasColumnName("rtsp_profile").HasMaxLength(64);
        builder.Property(x => x.RtspPath).HasColumnName("rtsp_path").HasMaxLength(256);
        builder.Property(x => x.CameraSeries).HasColumnName("camera_series").HasMaxLength(32);
        builder.Property(x => x.CameraModel).HasColumnName("camera_model").HasMaxLength(128);
        builder.Property(x => x.Enabled).HasColumnName("enabled");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}

