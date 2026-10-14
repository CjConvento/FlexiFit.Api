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
        private readonly IBlobService _blobService;
        private readonly Storage _storage;
        private readonly ILogger<MigrationController> _logger;
        private readonly MigrationService _migrationService;

        public MigrationController(
            IBlobService blobService,
            IConfiguration config,
            ILogger<MigrationController> logger,
            MigrationService migrationService)
        {
            _blobService = blobService;
            _logger = logger;
            _migrationService = migrationService;

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
            _logger.LogInformation("Starting Workouts migration (dryRun: {DryRun}, cap: {Cap})", dryRun, cap);
            var result = await _migrationService.MigrateWorkoutsAsync(dryRun, cap);
            return Ok(result);
        }

        // Foods: "food_387_..." → id = 387
        [HttpPost("foods")]
        public async Task<IActionResult> MigrateFoods(
            [FromQuery] bool dryRun = false,
            [FromQuery] int? cap = null)
        {
            _logger.LogInformation("Starting Workouts migration (dryRun: {DryRun}, cap: {Cap})", dryRun, cap);
            var result = await _migrationService.MigrateWorkoutsAsync(dryRun, cap);
            return Ok(result);
        }
    }
}