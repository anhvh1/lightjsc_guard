using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LightJSC.Infrastructure.Data;

public sealed class IngestorDbContext : DbContext
{
    public IngestorDbContext(DbContextOptions<IngestorDbContext> options) : base(options)
    {
    }

    public DbSet<CameraCredential> Cameras => Set<CameraCredential>();
    public DbSet<Subscriber> Subscribers => Set<Subscriber>();
    public DbSet<DlqMessage> DlqMessages => Set<DlqMessage>();
    public DbSet<RuntimeState> RuntimeStates => Set<RuntimeState>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<FaceTemplate> FaceTemplates => Set<FaceTemplate>();
    public DbSet<FaceEventRecord> FaceEvents => Set<FaceEventRecord>();
    public DbSet<MapLayout> MapLayouts => Set<MapLayout>();
    public DbSet<MapCameraPosition> MapCameraPositions => Set<MapCameraPosition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IngestorDbContext).Assembly);
    }
}

