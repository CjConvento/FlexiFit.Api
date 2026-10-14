using FlexiFit.Api.Extensions;
using FlexiFit.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace FlexiFit.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserProgramController : ControllerBase
    {
        private readonly ILogger<UserProgramController> _logger;
        private readonly ProgramService _programService;

        public UserProgramController( 
            ILogger<UserProgramController> logger,
            ProgramService programService)
        {
            _logger = logger;
            _programService = programService;
        }

        [HttpPost("check-progression")]
        public async Task<IActionResult> CheckProgression()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            // Delegate the logic to the service (ProgramService)
            var result = await _programService.CheckProgressionAsync(userId.Value); 

            return Ok(result);
        }

        [HttpPost("debug/fast-forward-to-end")]
        [Authorize(Roles = "ADMIN")]
        public async Task<IActionResult> FastForwardToEnd()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var result = await _programService.FastForwardToEndAsync(userId.Value);

            return Ok(result);
        }
    }
}