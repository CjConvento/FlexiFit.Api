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
    private readonly FlexiFitDbContext _db;
    private readonly FirebaseTokenVerifier _firebase;
    private readonly JwtService _jwt;

    public AuthController(FlexiFitDbContext db, JwtService jwt, FirebaseTokenVerifier firebase, DeviceTokenService deviceTokenService)
    {
        _db = db;
        _jwt = jwt;
        _firebase = firebase;
        _deviceTokenService = deviceTokenService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest req)
    {
        var decoded = await _firebase.VerifyAsync(req.FirebaseIdToken);
        var firebaseUid = decoded.Uid;
        var email = decoded.Claims.ContainsKey("email") ? decoded.Claims["email"].ToString() : "";

        var existingUser = await _db.UsrUsers.FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid);
        if (existingUser != null) return Ok(MapToAuthResponse(existingUser));

        // Fallback logic para sa auto-registration
        var finalName = req.Name ?? "FlexiFit User";
        var finalUsername = req.Username ?? "user_" + Guid.NewGuid().ToString("N").Substring(0, 7);

        var user = new UsrUser
        {
            FirebaseUid = firebaseUid,
            Email = email,
            Name = finalName,
            Username = finalUsername,
            Role = "USER",
            Status = "PENDING_ONBOARDING",
            CreatedAt = DateTime.UtcNow,
            IsVerified = true,
            AuthProvider = "FIREBASE"
        };

        _db.UsrUsers.Add(user);
        await _db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(req.FcmToken))
            await _deviceTokenService.UpsertAsync(user.UserId, req.FcmToken, "android");

        return Ok(MapToAuthResponse(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest req)
    {
        var decoded = await _firebase.VerifyAsync(req.FirebaseIdToken);
        var user = await _db.UsrUsers.FirstOrDefaultAsync(u => u.FirebaseUid == decoded.Uid);

        if (user == null) return Unauthorized(new { message = "User not found. Please register." });

        if (!string.IsNullOrWhiteSpace(req.FcmToken))
            await _deviceTokenService.UpsertAsync(user.UserId, req.FcmToken, "android");

        return Ok(MapToAuthResponse(user));
    }

    private AuthResponse MapToAuthResponse(UsrUser user)
    {
        var token = _jwt.CreateToken(user.UserId, user.FirebaseUid, user.Role, user.Email ?? "");
        return new AuthResponse(token, user.UserId, user.Role ?? "USER", user.Status ?? "PENDING_ONBOARDING", user.IsVerified);
    }
}