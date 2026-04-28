using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LightJSC.Infrastructure.Data.Configurations;

public sealed class FaceEventRecordConfiguration : IEntityTypeConfiguration<FaceEventRecord>
{
    public void Configure(EntityTypeBuilder<FaceEventRecord> builder)
    {
        builder.ToTable("face_events");
        builder.HasKey(record => record.Id);

        builder.Property(record => record.CameraId).HasMaxLength(128);
        builder.Property(record => record.WatchlistEntryId).HasMaxLength(128);
        builder.Property(record => record.PersonId).HasMaxLength(256);
        builder.Property(record => record.Gender).HasMaxLength(50);
        builder.Property(record => record.Mask).HasMaxLength(50);
        builder.Property(record => record.BestshotPath).HasMaxLength(1024);
        builder.Property(record => record.ThumbPath).HasMaxLength(1024);
        builder.Property(record => record.PersonJson).HasColumnType("jsonb");
        builder.Property(record => record.BBoxJson).HasColumnType("jsonb");

        builder.HasIndex(record => record.EventTimeUtc);
        builder.HasIndex(record => record.CameraId);
        builder.HasIndex(record => record.PersonId);
        builder.HasIndex(record => record.IsKnown);
        builder.HasIndex(record => record.WatchlistEntryId);
    }
}
