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

    public AuthController(
        FlexiFitDbContext db,
        JwtService jwt,
        FirebaseTokenVerifier firebase,
        DeviceTokenService deviceTokenService)
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

        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest("Name is required.");

        if (string.IsNullOrWhiteSpace(req.Username))
            return BadRequest("Username is required.");

        var decoded = await _firebase.VerifyAsync(req.FirebaseIdToken);
        var firebaseUid = decoded.Uid;

        decoded.Claims.TryGetValue("email", out var emailObj);
        var email = emailObj?.ToString();

        if (string.IsNullOrWhiteSpace(email))
            return BadRequest("Email not found in Firebase token.");

        var now = DateTime.UtcNow;

        // 1) Existing by Firebase UID
        var existingByFirebaseUid = await _db.UsrUsers
            .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid);

        if (existingByFirebaseUid != null)
        {
            existingByFirebaseUid.Name = req.Name;
            existingByFirebaseUid.Username = req.Username;
            existingByFirebaseUid.Email = email;
            existingByFirebaseUid.AuthProvider = "GOOGLE";
            existingByFirebaseUid.IsVerified = true;
            existingByFirebaseUid.UpdatedAt = now;

            await _db.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(req.FcmToken))
            {
                await _deviceTokenService.UpsertAsync(
                    existingByFirebaseUid.UserId,
                    req.FcmToken,
                    "android"
                );
            }

            var tokenExisting = _jwt.CreateToken(
                existingByFirebaseUid.UserId,
                existingByFirebaseUid.FirebaseUid,
                existingByFirebaseUid.Role,
                existingByFirebaseUid.Email
            );

            return Ok(new AuthResponse(
                tokenExisting,
                existingByFirebaseUid.UserId,
                existingByFirebaseUid.Role,
                existingByFirebaseUid.Status,
                existingByFirebaseUid.IsVerified
            ));
        }

        // 2) Existing by Email
        var existingByEmail = await _db.UsrUsers
            .FirstOrDefaultAsync(u => u.Email == email);

        if (existingByEmail != null)
        {
            var usernameUsedByOther = await _db.UsrUsers.AnyAsync(u =>
                u.Username == req.Username && u.UserId != existingByEmail.UserId);

            if (usernameUsedByOther)
                return BadRequest("Username is already taken.");

            existingByEmail.FirebaseUid = firebaseUid;
            existingByEmail.Name = req.Name;
            existingByEmail.Username = req.Username;
            existingByEmail.AuthProvider = "GOOGLE";
            existingByEmail.IsVerified = true;
            existingByEmail.UpdatedAt = now;

            await _db.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(req.FcmToken))
            {
                await _deviceTokenService.UpsertAsync(
                    existingByEmail.UserId,
                    req.FcmToken,
                    "android"
                );
            }

            var tokenExisting = _jwt.CreateToken(
                existingByEmail.UserId,
                existingByEmail.FirebaseUid,
                existingByEmail.Role,
                existingByEmail.Email
            );

            return Ok(new AuthResponse(
                tokenExisting,
                existingByEmail.UserId,
                existingByEmail.Role,
                existingByEmail.Status,
                existingByEmail.IsVerified
            ));
        }

        // 3) Prevent duplicate username for new users
        var usernameTaken = await _db.UsrUsers
            .AnyAsync(u => u.Username == req.Username);

        if (usernameTaken)
            return BadRequest("Username is already taken.");

        // 4) Create new user
        var user = new UsrUser
        {
            FirebaseUid = firebaseUid,
            Email = email,
            Name = req.Name,
            Username = req.Username,
            IsVerified = true,
            Role = "USER",
            Status = "ACTIVE",
            AuthProvider = "GOOGLE",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.UsrUsers.Add(user);
        await _db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(req.FcmToken))
        {
            await _deviceTokenService.UpsertAsync(
                user.UserId,
                req.FcmToken,
                "android"
            );
        }

        var token = _jwt.CreateToken(
            user.UserId,
            user.FirebaseUid,
            user.Role,
            user.Email
        );

        return Ok(new AuthResponse(
            token,
            user.UserId,
            user.Role,
            user.Status,
            user.IsVerified
        ));
    }

    // POST: /api/auth/login
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FirebaseIdToken))
            return BadRequest("FirebaseIdToken is required.");

        var decoded = await _firebase.VerifyAsync(req.FirebaseIdToken);
        var firebaseUid = decoded.Uid;

        var user = await _db.UsrUsers
            .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid);

        if (user == null)
            return Unauthorized("User not registered in FlexiFit database. Call /api/auth/register first.");

        if (!string.Equals(user.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            return Unauthorized("Account is not active.");

        if (!string.IsNullOrWhiteSpace(req.FcmToken))
        {
            await _deviceTokenService.UpsertAsync(user.UserId, req.FcmToken, "android");
        }

        var token = _jwt.CreateToken(
            user.UserId,
            user.FirebaseUid,
            user.Role,
            user.Email
        );

        return Ok(new AuthResponse(
            token,
            user.UserId,
            user.Role,
            user.Status,
            user.IsVerified
        ));
    }
}