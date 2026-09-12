using System.Text.RegularExpressions;
using Appwrite;
using Appwrite.Services;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Controllers
{
    /// <summary>
    /// ONE-TIME MIGRATION: Updates DB img_filename columns with full Appwrite URLs.
    /// Idempotent, supports dryRun + cap. DELETE THIS FILE AFTER MIGRATION.
    /// </summary>
    [ApiController]
    [Route("api/admin/migration")]
    [Authorize(Roles = "ADMIN")]
    public class MigrationController : ControllerBase
    {
        private readonly FlexiFitDbContext _db;
        private readonly IBlobService _blobService;
        private readonly Storage _storage;
        private readonly ILogger<MigrationController> _logger;

        public MigrationController(
            FlexiFitDbContext db,
            IBlobService blobService,
            IConfiguration config,
            ILogger<MigrationController> logger)
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

        // Workouts: "96_muscle_gain_..." → id = 96
        // cap: process only N files (null = all)
        [HttpPost("workouts")]
        public async Task<IActionResult> MigrateWorkouts(
            [FromQuery] bool dryRun = false,
            [FromQuery] int? cap = null)
        {
            var updated = 0; var skipped = 0; var notFound = 0;
            var invalidName = 0; var totalScanned = 0;
            var samples = new List<object>();
            var limit = 100; var offset = 0;
            var stop = false;

            try
            {
                while (!stop)
                {
                    var files = await _storage.ListFiles(
                        bucketId: "workouts",
                        queries: new List<string> { Query.Limit(limit), Query.Offset(offset) }
                    );

                    if (files.Files.Count == 0) break;

                    foreach (var file in files.Files)
                    {
                        totalScanned++;

                        var match = Regex.Match(file.Name, @"^(\d+)_");
                        if (!match.Success)
                        {
                            invalidName++;
                        }
                        else
                        {
                            var workoutId = int.Parse(match.Groups[1].Value);
                            var newUrl = _blobService.GetFileUrl(file.Id, "workouts");

                            var workout = await _db.WrkWorkouts
                                .FirstOrDefaultAsync(w => w.WorkoutId == workoutId);

                            if (workout == null) { notFound++; }
                            else if (workout.ImgFilename == newUrl) { skipped++; }
                            else
                            {
                                if (samples.Count < 3)
                                {
                                    samples.Add(new {
                                        workoutId,
                                        fileName = file.Name,
                                        oldValue = workout.ImgFilename,
                                        newValue = newUrl
                                    });
                                }

                                if (!dryRun) workout.ImgFilename = newUrl;
                                updated++;
                            }
                        }

                        // Stop check AFTER processing each file
                        if (cap.HasValue && totalScanned >= cap.Value)
                        {
                            stop = true;
                            break;
                        }
                    }

                    if (files.Files.Count < limit) break;
                    offset += limit;
                }

                if (!dryRun && updated > 0) await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "Workouts migration: dryRun={DryRun} cap={Cap} scanned={Scanned} updated={Updated} skipped={Skipped} notFound={NotFound} invalid={Invalid}",
                    dryRun, cap, totalScanned, updated, skipped, notFound, invalidName);

                return Ok(new {
                    bucket = "workouts",
                    dryRun,
                    capApplied = cap,
                    totalScanned,
                    updated,
                    skipped,
                    notFound,
                    invalidName,
                    sampleChanges = samples
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workouts migration failed");
                return StatusCode(500, new { error = ex.Message, inner = ex.InnerException?.Message });
            }
        }

        // Foods: "food_387_..." → id = 387
        [HttpPost("foods")]
        public async Task<IActionResult> MigrateFoods(
            [FromQuery] bool dryRun = false,
            [FromQuery] int? cap = null)
        {
            var updated = 0; var skipped = 0; var notFound = 0;
            var invalidName = 0; var totalScanned = 0;
            var samples = new List<object>();
            var limit = 100; var offset = 0;
            var stop = false;

            try
            {
                while (!stop)
                {
                    var files = await _storage.ListFiles(
                        bucketId: "foods",
                        queries: new List<string> { Query.Limit(limit), Query.Offset(offset) }
                    );

                    if (files.Files.Count == 0) break;

                    foreach (var file in files.Files)
                    {
                        totalScanned++;

                        var match = Regex.Match(file.Name, @"^food_(\d+)_", RegexOptions.IgnoreCase);
                        if (!match.Success)
                        {
                            invalidName++;
                        }
                        else
                        {
                            var foodId = int.Parse(match.Groups[1].Value);
                            var newUrl = _blobService.GetFileUrl(file.Id, "foods");

                            var food = await _db.NtrFoodItems
                                .FirstOrDefaultAsync(f => f.FoodId == foodId);

                            if (food == null) { notFound++; }
                            else if (food.ImgFilename == newUrl) { skipped++; }
                            else
                            {
                                if (samples.Count < 3)
                                {
                                    samples.Add(new {
                                        foodId,
                                        fileName = file.Name,
                                        oldValue = food.ImgFilename,
                                        newValue = newUrl
                                    });
                                }

                                if (!dryRun) food.ImgFilename = newUrl;
                                updated++;
                            }
                        }

                        if (cap.HasValue && totalScanned >= cap.Value)
                        {
                            stop = true;
                            break;
                        }
                    }

                    if (files.Files.Count < limit) break;
                    offset += limit;
                }

                if (!dryRun && updated > 0) await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "Foods migration: dryRun={DryRun} cap={Cap} scanned={Scanned} updated={Updated} skipped={Skipped} notFound={NotFound} invalid={Invalid}",
                    dryRun, cap, totalScanned, updated, skipped, notFound, invalidName);

                return Ok(new {
                    bucket = "foods",
                    dryRun,
                    capApplied = cap,
                    totalScanned,
                    updated,
                    skipped,
                    notFound,
                    invalidName,
                    sampleChanges = samples
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Foods migration failed");
                return StatusCode(500, new { error = ex.Message, inner = ex.InnerException?.Message });
            }
        }
    }
}