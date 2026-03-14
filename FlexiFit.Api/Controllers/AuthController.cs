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

    // POST: /api/auth/register
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FirebaseIdToken))
            return BadRequest("FirebaseIdToken is required.");

        var decoded = await _firebase.VerifyAsync(req.FirebaseIdToken);
        var firebaseUid = decoded.Uid;

        // Kunin ang email mula sa Firebase claims
        decoded.Claims.TryGetValue("email", out var emailObj);
        var email = emailObj?.ToString();

        if (string.IsNullOrWhiteSpace(email))
            return BadRequest("Email not found in Firebase token.");

        // Siguraduhin na ALL CAPS ang provider para sa SQL Constraint mo
        var provider = string.IsNullOrWhiteSpace(req.AuthProvider) ? "EMAIL" : req.AuthProvider.ToUpper();

        var now = DateTime.UtcNow;

        // 1) Check kung may existing user na gamit ang Firebase UID
        var existingUser = await _db.UsrUsers
            .Include(u => u.UsrUserProfile) // Isama ang profile para sa PhotoUrl
            .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid || u.Email == email);

        if (existingUser != null)
        {
            // Update user info if needed
            existingUser.FirebaseUid = firebaseUid; // Siguraduhin na match ang UID
            existingUser.AuthProvider = provider;
            existingUser.IsVerified = true;
            existingUser.UpdatedAt = now;

            await _db.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(req.FcmToken))
                await _deviceTokenService.UpsertAsync(existingUser.UserId, req.FcmToken, "android");

            return Ok(MapToAuthResponse(existingUser));
        }

        // 2) Check if Username is taken by another person
        if (await _db.UsrUsers.AnyAsync(u => u.Username == req.Username))
            return BadRequest("Username is already taken.");

        // 3) Create New User
        var user = new UsrUser
        {
            FirebaseUid = firebaseUid,
            Email = email,
            Name = req.Name ?? "FlexiFit User",
            Username = req.Username ?? "user_" + Guid.NewGuid().ToString("N").Substring(0, 7),
            IsVerified = true,
            Role = "USER",
            Status = "PENDING_ONBOARDING", // Default for new users
            AuthProvider = provider,
            CreatedAt = now,
            UpdatedAt = now
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

        // Include the Profile so we can get the AvatarUrl
        var user = await _db.UsrUsers
            .Include(u => u.UsrUserProfile)
            .FirstOrDefaultAsync(u => u.FirebaseUid == decoded.Uid);

        if (user == null)
            return Unauthorized("User not registered. Please sign up first.");

        if (!string.IsNullOrWhiteSpace(req.FcmToken))
            await _deviceTokenService.UpsertAsync(user.UserId, req.FcmToken, "android");

        return Ok(MapToAuthResponse(user));
    }

    private AuthResponse MapToAuthResponse(UsrUser user)
    {
        var token = _jwt.CreateToken(
            user.UserId,
            user.FirebaseUid,
            user.Role,
            user.Email
    );

        // Dahil WithOne ang relationship sa fluent API mo, 
        // diretso na tayo sa .AvatarUrl, walang FirstOrDefault()
        var photo = user.UsrUserProfile?.AvatarUrl;

        return new AuthResponse(
            token,                          // 1
            user.UserId,                    // 2
            user.Role ?? "USER",            // 3
            user.Status ?? "PENDING_ONBOARDING", // 4
            user.IsVerified,                // 5
            user.Name,                      // 6
            photo                       // 7
        );
    }
}