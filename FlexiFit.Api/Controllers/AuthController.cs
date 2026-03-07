using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly DeviceTokenService _deviceTokenService;
    private readonly FlexifitDbContext _db;
    private readonly FirebaseTokenVerifier _firebase;
    private readonly JwtService _jwt;

    public AuthController(
        FlexifitDbContext db,
        JwtService jwt,
        FirebaseTokenVerifier firebase,
        DeviceTokenService deviceTokenService)   // ADD THIS
    {
        _db = db;
        _jwt = jwt;
        _firebase = firebase;
        _deviceTokenService = deviceTokenService; // ASSIGN THIS
    }

    // POST: /api/auth/register
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest req)
    {
        var decoded = await _firebase.VerifyAsync(req.FirebaseIdToken);

        var firebaseUid = decoded.Uid;

        // email is optional in your table
        decoded.Claims.TryGetValue("email", out var emailObj);
        var email = emailObj?.ToString();

        // If already exists -> idempotent (return token)
        var existing = await _db.UsrUsers.FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid);
        if (existing != null)
        {
            var tokenExisting = _jwt.CreateToken(existing.UserId, existing.FirebaseUid, existing.Role, existing.Email);
            return Ok(new AuthResponse(tokenExisting, existing.UserId, existing.Role, existing.Status, existing.IsVerified));
        }

        var now = DateTime.UtcNow;

        var user = new UsrUser
        {
            FirebaseUid = firebaseUid,
            Email = email,
            Name = req.Name,
            Username = req.Username,
            Address = req.Address,
            IsVerified = false,
            Role = "USER",
            Status = "ACTIVE",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.UsrUsers.Add(user);
        await _db.SaveChangesAsync();

        var token = _jwt.CreateToken(user.UserId, user.FirebaseUid, user.Role, user.Email);
        return Ok(new AuthResponse(token, user.UserId, user.Role, user.Status, user.IsVerified));
    }

// POST: /api/auth/login
[HttpPost("login")]
public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest req)
{
    var decoded = await _firebase.VerifyAsync(req.FirebaseIdToken);
    var firebaseUid = decoded.Uid;

    var user = await _db.UsrUsers.FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid);
    if (user == null)
        return Unauthorized("User not registered in FlexiFit database. Call /api/auth/register first.");

    if (!string.Equals(user.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        return Unauthorized("Account is not active.");

    // ✅ SAVE/UPDATE FCM TOKEN HERE (before creating/returning response)
    if (!string.IsNullOrWhiteSpace(req.FcmToken))
    {
        await _deviceTokenService.UpsertAsync(user.UserId, req.FcmToken, "android");
    }

    var token = _jwt.CreateToken(user.UserId, user.FirebaseUid, user.Role, user.Email);
    return Ok(new AuthResponse(token, user.UserId, user.Role, user.Status, user.IsVerified));
}
}