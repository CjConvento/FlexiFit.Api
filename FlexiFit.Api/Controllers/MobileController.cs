using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Identity.Client;
using FlexiFit.Api.Mappers;
using FlexiFit.Api.Extensions;
using Npgsql;
using System.Security.Claims;

namespace FlexiFit.Api.Controllers
{
    [ApiController]
    [Route("api/mobile")]
    public class MobileController : ControllerBase
    {
        private readonly ILogger<MobileController> _logger;
        private readonly MobileService _mobileService;

        public MobileController(
            ILogger<MobileController> logger,
            MobileService mobileService
            )
        {
            _logger = logger;
            _mobileService = mobileService;
        }

        [Authorize]
        [HttpGet("bootstrap")]
        public async Task<IActionResult> Bootstrap()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();



            var response = await _mobileService.GetBootstrapAsync(userId.Value);
            return Ok(response);
        }

        [Authorize]
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var response = await _mobileService.GetDashboardAsync(userId.Value, baseUrl);

            return Ok(response);
        }

        [Authorize]
        [HttpPost("onboarding/profile")]
        public async Task<IActionResult> SubmitOnboardingProfile([FromBody] OnboardingProfileRequest request)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var response = await _mobileService.SubmitOnboardingProfileAsync(userId.Value, request);
            return Ok(response);
        }

        [Authorize]
        [HttpDelete("debug/reset-onboarding/{userId}")]
        public async Task<IActionResult> ResetOnboarding(int userId)
        {
            var currentUserId = User.GetUserId();
            if (currentUserId != userId) return Forbid();

            var response = await _mobileService.ResetOnboardingAsync(userId);
            return Ok(response);
        }
    }
}