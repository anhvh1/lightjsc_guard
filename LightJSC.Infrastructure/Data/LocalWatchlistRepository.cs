using System.Runtime.InteropServices;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using LightJSC.Infrastructure.Vector;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LightJSC.Infrastructure.Data;

public sealed class LocalWatchlistRepository : IWatchlistRepository
{
    private readonly IngestorDbContext _dbContext;
    private readonly EnrollmentOptions _enrollmentOptions;

    public LocalWatchlistRepository(IngestorDbContext dbContext, IOptions<EnrollmentOptions> enrollmentOptions)
    {
        _dbContext = dbContext;
        _enrollmentOptions = enrollmentOptions.Value;
    }

    public async Task<IReadOnlyList<WatchlistEntry>> FetchUpdatedAsync(DateTime sinceUtc, CancellationToken cancellationToken)
    {
        var rows = await QueryEntries()
            .Where(x => x.TemplateUpdatedAt > sinceUtc || x.PersonUpdatedAt > sinceUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(ToEntry).Where(x => x.FeatureBytes.Length > 0).ToList();
    }

    public async Task<IReadOnlyList<WatchlistEntry>> FetchAllActiveAsync(CancellationToken cancellationToken)
    {
        var rows = await QueryEntries()
            .ToListAsync(cancellationToken);

        return rows.Select(ToEntry).Where(x => x.IsActive && x.FeatureBytes.Length > 0).ToList();
    }

    private IQueryable<LocalWatchlistRow> QueryEntries()
    {
        return from template in _dbContext.FaceTemplates.AsNoTracking()
            join person in _dbContext.Persons.AsNoTracking() on template.PersonId equals person.Id
            where template.IsActive && person.IsActive
            select new LocalWatchlistRow
            {
                TemplateId = template.Id,
                FeatureVersion = template.FeatureVersion,
                FeatureBytes = template.FeatureBytes,
                TemplateUpdatedAt = template.UpdatedAt,
                PersonUpdatedAt = person.UpdatedAt,
                PersonCode = person.Code,
                FirstName = person.FirstName,
                LastName = person.LastName,
                Gender = person.Gender,
                Age = person.Age,
                Remarks = person.Remarks,
                Category = person.Category,
                ListType = person.ListType
            };
    }

    private WatchlistEntry ToEntry(LocalWatchlistRow row)
    {
        var bytes = row.FeatureBytes ?? Array.Empty<byte>();
        var vector = bytes.Length == 0 || bytes.Length % sizeof(float) != 0
            ? Array.Empty<float>()
            : MemoryMarshal.Cast<byte, float>(bytes).ToArray();

        if (vector.Length > 0)
        {
            VectorMath.NormalizeInPlace(vector);
        }

        return new WatchlistEntry
        {
            EntryId = row.TemplateId.ToString(),
            PersonId = BuildPersonDisplayName(row.FirstName, row.LastName, row.PersonCode),
            Person = new PersonProfile
            {
                Code = row.PersonCode,
                FirstName = row.FirstName,
                LastName = row.LastName,
                Gender = row.Gender,
                Age = row.Age,
                Remarks = row.Remarks,
                Category = row.Category,
                ListType = row.ListType
            },
            FeatureVersion = ResolveFeatureVersion(row.FeatureVersion),
            FeatureBytes = bytes,
            FeatureVector = vector,
            SimilarityThreshold = 0f,
            UpdatedAt = row.TemplateUpdatedAt,
            IsActive = true
        };
    }

    private string ResolveFeatureVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return _enrollmentOptions.FeatureVersion;
        }

        if (!string.IsNullOrWhiteSpace(_enrollmentOptions.AppName)
            && string.Equals(version, _enrollmentOptions.AppName, StringComparison.OrdinalIgnoreCase))
        {
            return _enrollmentOptions.FeatureVersion;
        }

        return version.Trim();
    }

    private static string? BuildPersonDisplayName(string? firstName, string? lastName, string? code)
    {
        var first = string.IsNullOrWhiteSpace(firstName) ? null : firstName.Trim();
        var last = string.IsNullOrWhiteSpace(lastName) ? null : lastName.Trim();

        if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(last))
        {
            return $"{first} {last}";
        }

        if (!string.IsNullOrWhiteSpace(first))
        {
            return first;
        }

        if (!string.IsNullOrWhiteSpace(last))
        {
            return last;
        }

        return string.IsNullOrWhiteSpace(code) ? null : code.Trim();
    }

    private sealed class LocalWatchlistRow
    {
        public Guid TemplateId { get; init; }
        public string FeatureVersion { get; init; } = string.Empty;
        public byte[]? FeatureBytes { get; init; }
        public DateTime TemplateUpdatedAt { get; init; }
        public DateTime PersonUpdatedAt { get; init; }
        public string? PersonCode { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public string? Gender { get; init; }
        public int? Age { get; init; }
        public string? Remarks { get; init; }
        public string? Category { get; init; }
        public string? ListType { get; init; }
    }
}

