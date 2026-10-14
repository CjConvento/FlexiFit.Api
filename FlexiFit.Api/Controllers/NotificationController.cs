using System.Security.Claims;
using FlexiFit.Api.Dtos;
using FlexiFit.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlexiFit.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly NotificationService _notificationService;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(
            NotificationService notificationService,
            ILogger<NotificationsController> logger)
        {
            _notificationService = notificationService;
            _logger = logger;
        }

        // ==========================================
        // 1. SETTINGS ENDPOINTS
        // ==========================================

        [HttpGet("settings")]
        public async Task<ActionResult<NotificationSettingsDto>> GetSettings()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

            var settings = await _notificationService.GetSettingsAsync(userId.Value);
            return Ok(settings);
        }

        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings([FromBody] NotificationSettingsDto dto)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

            await _notificationService.UpdateSettingsAsync(userId.Value, dto);
            return NoContent();
        }

        // ==========================================
        // 2. NOTIFICATION HISTORY ENDPOINTS
        // ==========================================

        [HttpGet("history")]
        public async Task<ActionResult<IEnumerable<NotificationHistoryDto>>> GetNotificationHistory()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

            var history = await _notificationService.GetNotificationHistoryAsync(userId.Value);
            return Ok(history);
        }
    }
}   