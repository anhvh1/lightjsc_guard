using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace LightJSC.Infrastructure.Data;

public sealed class IngestorDbContextFactory : IDesignTimeDbContextFactory<IngestorDbContext>
{
    public IngestorDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Development";

        var basePath = ResolveApiSettingsPath();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? configuration["Postgres:ConnectionString"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = Environment.GetEnvironmentVariable("IPRO_POSTGRES_CONNECTION");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Postgres connection string not found in appsettings.json.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<IngestorDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new IngestorDbContext(optionsBuilder.Options);
    }

    private static string ResolveApiSettingsPath()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "LightJSC.Api", "appsettings.json");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate) ?? current.FullName;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate LightJSC.Api/appsettings.json.");
    }
}

