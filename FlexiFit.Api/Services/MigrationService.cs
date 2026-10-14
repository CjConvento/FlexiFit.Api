using Appwrite;
using Appwrite.Services;
using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace FlexiFit.Api.Services;

public class MigrationService
{
    private readonly FlexiFitDbContext _db;
    private readonly IBlobService _blobService;
    private readonly Storage _storage;
    private readonly ILogger<MigrationService> _logger;

    public MigrationService(
        FlexiFitDbContext db,
        IBlobService blobService,
        IConfiguration config,
        ILogger<MigrationService> logger)
    {
        _db = db;
        _blobService = blobService;
        _logger = logger;

        var client = new Client()
            .SetEndpoint(config["StorageSettings:Appwrite:Endpoint"]!)
            .SetProject(config["StorageSettings:Appwrite:ProjectId"]!)
            .SetKey(config["StorageSettings:Appwrite:ApiKey"]!);

        _storage = new Storage(client);
    }

    public async Task<object> MigrateWorkoutsAsync(bool dryRun, int? cap)
    {
        return await ExecuteMigrationAsync<WrkWorkout>(
            bucket: "workouts",
            regexPattern: @"^(\d+)_",
            queryBuilder: db => db.WrkWorkouts,
            getId: w => w.WorkoutId,
            getUrl: w => w.ImgFilename ?? "",       // Type-safe getter
            setUrl: (w, url) => w.ImgFilename = url, // Type-safe setter
            dryRun,
            cap
        );
    }

    public async Task<object> MigrateFoodsAsync(bool dryRun, int? cap)
    {
        return await ExecuteMigrationAsync<NtrFoodItem>(
            bucket: "foods",
            regexPattern: @"^food_(\d+)_",
            queryBuilder: db => db.NtrFoodItems,
            getId: f => f.FoodId,
            getUrl: f => f.ImgFilename ?? "",       // Type-safe getter
            setUrl: (f, url) => f.ImgFilename = url, // Type-safe setter
            dryRun,
            cap
        );
    }

    // 🔥 SHARED GENERIC LOGIC: DRY, N+1 Free, at Type-Safe
    private async Task<object> ExecuteMigrationAsync<T>(
        string bucket,
        string regexPattern,
        Func<FlexiFitDbContext, IQueryable<T>> queryBuilder,
        Func<T, int> getId,
        Func<T, string> getUrl,       // <-- BAGONG DAGDAG: Para makuha ang current URL nang walang reflection
        Action<T, string> setUrl,
        bool dryRun,
        int? cap) where T : class
    {
        int updated = 0, skipped = 0, notFound = 0, invalidName = 0, totalScanned = 0;
        var samples = new List<object>();
        var invalidFiles = new List<string>();
        int limit = 100, offset = 0;
        bool stop = false;

        while (!stop)
        {
            var files = await _storage.ListFiles(
                bucketId: bucket,
                queries: new List<string> { Query.Limit(limit), Query.Offset(offset) }
            );

            if (files.Files.Count == 0) break;

            // 1. Kunin lahat ng IDs at New URLs sa batch na ito (Iwas N+1 Query)
            var idMap = new Dictionary<int, (string FileName, string NewUrl)>();
            
            foreach (var file in files.Files)
            {
                totalScanned++;
                var match = Regex.Match(file.Name, regexPattern, RegexOptions.IgnoreCase);
                
                if (!match.Success)
                {
                    invalidName++;
                    invalidFiles.Add(file.Name);
                }
                else
                {
                    int id = int.Parse(match.Groups[1].Value);
                    idMap[id] = (file.Name, _blobService.GetFileUrl(file.Id, bucket));
                }

                if (cap.HasValue && totalScanned >= cap.Value) { stop = true; break; }
            }

            // 2. I-batch query ang lahat ng entities na kailangan (Isang query lang per batch!)
            var targetIds = idMap.Keys.ToList();
            var entities = await queryBuilder(_db)
                .Where(e => targetIds.Contains(getId(e)))
                .ToListAsync(); // Tracked entities para gumana ang SaveChangesAsync

            var entityMap = entities.ToDictionary(getId);

            // 3. I-update sa memory (Exact same logic as original, pero mas malinis)
            foreach (var kvp in idMap)
            {
                int id = kvp.Key;
                var (fileName, newUrl) = kvp.Value;

                if (!entityMap.TryGetValue(id, out var entity))
                {
                    notFound++;
                }
                else
                {
                    // Gamit ang strongly-typed delegate, walang reflection
                    string currentUrl = getUrl(entity);
                    
                    if (currentUrl == newUrl) 
                    {
                        skipped++;
                    }
                    else
                    {
                        if (samples.Count < 3)
                        {
                            samples.Add(new { 
                                Id = id, 
                                FileName = fileName, 
                                OldValue = currentUrl,
                                NewValue = newUrl 
                            });
                        }

                        if (!dryRun) setUrl(entity, newUrl);
                        updated++;
                    }
                }
            }

            if (files.Files.Count < limit) break;
            offset += limit;
        }

        // 4. Isang SaveChangesAsync lang sa dulo ng buong process (or per batch kung gusto mo, pero end-of-process is fine for migration)
        if (!dryRun && updated > 0)
        {
            await _db.SaveChangesAsync();
        }

        _logger.LogInformation(
            "{Bucket} migration: dryRun={DryRun} cap={Cap} scanned={Scanned} updated={Updated} skipped={Skipped} notFound={NotFound} invalid={Invalid}",
            bucket, dryRun, cap, totalScanned, updated, skipped, notFound, invalidName);

        return new
        {
            bucket,
            dryRun,
            capApplied = cap,
            totalScanned,
            updated,
            skipped,
            notFound,
            invalidName,
            invalidFiles = invalidFiles.Take(10).ToList(),
            sampleChanges = samples
        };
    }
}