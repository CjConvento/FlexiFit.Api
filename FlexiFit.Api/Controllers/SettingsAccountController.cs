using System.Security.Claims;
using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FlexiFit.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/settings/account")]
    public class SettingsAccountController : ControllerBase
    {
        private readonly AccountService _accountService;

        public SettingsAccountController(AccountService accountService)
        {
            _accountService = accountService;
        }

        [HttpPost("reset-progress")]
        [Authorize]
        public async Task<IActionResult> ResetProgress()
        {
            var userId = await _accountService.ResolveUserIdFromClaimsAsync(User);
            if (userId == null) return Unauthorized();

            // tinatawag nalang service method para i-reset ang progress ng user
            await _accountService.ResetProgressAsync(userId.Value);

            return Ok(new { message = "Gudis na babe! Your workouts, achievements, and logs have been cleared." });
        }

        // PUT: api/settings/account/email (Para sa Manual Users)
        [HttpPut("email")]
        public async Task<IActionResult> UpdateEmail([FromBody] UpdateEmailDto dto)
        {
            var userId = await _accountService.ResolveUserIdFromClaimsAsync(User);
            if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

            await _accountService.UpdateEmailAsync(userId.Value, dto.NewEmail);

            return NoContent();
        }

        // PUT: api/settings/account/link-google (Para sa Google Users)
        [HttpPut("link-google")]
        public async Task<IActionResult> LinkGoogle([FromBody] UpdateGoogleEmailDto dto)
        {
            var userId = await _accountService.ResolveUserIdFromClaimsAsync(User);
            if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

            await _accountService.LinkGoogleAsync(userId.Value, dto.NewEmail, dto.NewFirebaseUid);

            return Ok(new { message = "Successfully linked to new Google account." });
        }

        // DELETE: api/settings/account/terminate
        [HttpDelete("terminate")]
        public async Task<IActionResult> DeleteAccount()
        {
            var userId = await _accountService.ResolveUserIdFromClaimsAsync(User);
            if (userId == null) return Unauthorized();

            await _accountService.DeleteAccountAsync(userId.Value);

            return Ok(new { message = "Account permanently deleted." });
        }

        // GET: api/settings/account/export
        [HttpGet("export")]
        public async Task<IActionResult> ExportData()
        {
            var userId = await _accountService.ResolveUserIdFromClaimsAsync(User);
            if (userId == null) return Unauthorized();

            var data = await _accountService.ExportDataAsync(userId.Value);

            return Ok(data);
        }
    }
}