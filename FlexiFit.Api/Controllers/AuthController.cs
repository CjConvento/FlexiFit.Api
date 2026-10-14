using Dapper;
using FirebaseAdmin.Auth;
using FlexiFit.Api.Dtos;
using FlexiFit.Api.Services;
using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace FlexiFit.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AuthService authService,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    // POST: /api/auth/register
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest req)
    {
        // Pure Orchestration: Middleware will map ArgumentException -> 400, InvalidOperationException -> 409
        var response = await _authService.RegisterAsync(req);
        return Ok(response);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest req)
    {
        // Middleware will map KeyNotFoundException -> 404
        var response = await _authService.LoginAsync(req);
        return Ok(response);
    }

    // POST: /api/auth/admin-login
    [HttpPost("admin-login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> AdminLogin([FromBody] AdminLoginRequest req)
    {
        // Middleware will map UnauthorizedAccessException -> 401
        var response = await _authService.AdminLoginAsync(req);
        return Ok(response);
    }

    // POST: /api/auth/refresh
    [HttpPost("refresh")]
    [AllowAnonymous] // Hindi kailangan ng JWT kasi expired na
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        // Middleware will handle all mapping
        var response = await _authService.RefreshTokenAsync(request.FirebaseIdToken);
        return Ok(response);
    }
}