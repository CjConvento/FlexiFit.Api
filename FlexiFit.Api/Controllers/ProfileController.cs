using System;
using Microsoft.AspNetCore.Mvc;
using FlexiFit.Api.Dtos;
using System.IO;
using System.Data;
using Npgsql;
using System.Linq;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using FlexiFit.Api.Services;
using FlexiFit.Api.Extensions;

namespace FlexiFit.Api.Controllers
{
    [ApiController]
    [Route("api/profile")]
    public class ProfileController : ControllerBase
    {
        private readonly ILogger<ProfileController> _logger;
        private readonly NutritionCalculator _nutritionCalculator;
        private readonly ProfileService _profileService;

        // Dito sa constructor, dapat dalawa na silang tinatanggap
        public ProfileController(
            ILogger<ProfileController> logger,
            NutritionCalculator nutritionCalculator,
            ProfileService profileService)
        {
            _logger = logger;
            _nutritionCalculator = nutritionCalculator;
            _profileService = profileService;
        }


        [Authorize]
        [HttpPut("update-full")]
        public async Task<IActionResult> UpdateFullProfile([FromBody] UpdateOnboardingRequest request)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

            var newCalories = await _profileService.UpdateFullProfileAsync(userId.Value, request);

            _logger.LogInformation("Profile updated successfully for user {UserId}", userId);
            return Ok(new { message = "Profile updated successfully!", newCalories });

        }

        [Authorize]
        [HttpPost("upload-avatar")]
        [Consumes("multipart/form-data")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> UploadAvatar(
            [FromForm] UploadAvatarForm form)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

            if (form?.File == null) return BadRequest(new { message = "No file uploaded." });

            // Ang file validation at Appwrite upload ay nasa loob na ng Service
            var avatarUrl = await _profileService.UploadAvatarAsync(userId.Value, form.File);

            _logger.LogInformation("Avatar uploaded successfully for user {UserId}", userId);
            return Ok(new { url = avatarUrl });
        }

        // Delegated from ProfileService after moving business logic there for better separation of concerns
        [Authorize]
        [HttpGet("details")]
        public async Task<IActionResult> GetProfileDetails()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var response = await _profileService.GetUserProfileAsync(userId.Value, baseUrl);
            return Ok(response);
        }

        [Authorize]
        [HttpPatch("weight")]
        public async Task<IActionResult> UpdateWeight([FromBody] UpdateWeightRequest request)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

            await _profileService.UpdateWeightAsync(userId.Value, request);
            
            _logger.LogInformation("Weight updated for user {UserId} to {Weight} kg", userId, request.NewWeight);
            return Ok(new { message = "Weight and nutrition targets updated successfully!" });
        }

        [HttpGet("full")]
        public async Task<IActionResult> GetFullProfile()
        {
            return await GetProfileDetails();
        }
    }
}