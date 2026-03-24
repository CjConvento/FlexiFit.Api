using Dapper;
using FirebaseAdmin.Auth;
using FlexiFit.Api.Dtos;
using FlexiFit.Api.Services;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
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
    private readonly DeviceTokenService _deviceTokenService;
    private readonly FlexiFitDbContext _db;
    private readonly FirebaseTokenVerifier _firebase;
    private readonly JwtService _jwt;
    private readonly IUserService _userService;
    private readonly IConfiguration _config;
    private readonly string _connectionString;

    public AuthController(
        FlexiFitDbContext db,
        JwtService jwt,
        FirebaseTokenVerifier firebase,
        DeviceTokenService deviceTokenService,
        IUserService userService,
        IConfiguration config)
    {
        _db = db;
        _jwt = jwt;
        _firebase = firebase;
        _deviceTokenService = deviceTokenService;
        _userService = userService;
        _config = config;
        _connectionString = _config.GetConnectionString("FlexifitDb");
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

    [HttpPost("logintoken")]
    public async Task<IActionResult> LoginToken([FromBody] TokenAuth request)
    {
        using var connection = new SqlConnection(_connectionString);

        try
        {
            var user = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT * FROM dbo.usr_users WHERE email = @email AND firebase_uid = @uid",
                new { email = request.Email.Trim(), uid = request.FirebaseUid.Trim() });

            if (user == null)
            {
                return Unauthorized(new { message = "User not found or UID mismatch." });
            }

            // PAKIAYOS DITO: user_id ang gamitin, hindi id
            string userIdString = user.user_id.ToString();
            string firebaseUidString = user.firebase_uid.ToString();

            // Ipasa sa generator
            var token = GenerateJwtToken(request.Email, userIdString, firebaseUidString);

            return Ok(new { token = token });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Database Error: {ex.Message}");
        }
    }

    // POST: /api/auth/refresh
    [HttpPost("refresh")]
    [AllowAnonymous] // Hindi kailangan ng JWT kasi expired na
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrEmpty(request.FirebaseIdToken))
            return BadRequest("Firebase token is required.");

        // 1. Verify Firebase ID token
        FirebaseToken decodedToken;
        try
        {
            decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(request.FirebaseIdToken);
        }
        catch (FirebaseAuthException ex)
        {
            return Unauthorized(new { message = "Invalid Firebase token.", error = ex.Message });
        }

        string firebaseUid = decodedToken.Uid;

        // 2. Fetch user from database using IUserService
        var user = await _userService.GetByFirebaseUidAsync(firebaseUid);
        if (user == null)
        {
            return NotFound(new { message = "User not found. Please register first." });
        }

        // 3. Generate new JWT token using JwtService (same as in MapToAuthResponse)
        var token = _jwt.CreateToken(
            user.UserId,
            user.FirebaseUid,
            user.Role,
            user.Email
        );

        // 4. Build response
        var photo = user.UsrUserProfile?.AvatarUrl;
        var response = new AuthResponse(
            token,
            user.UserId,
            user.Role ?? "USER",
            user.Status ?? "PENDING_ONBOARDING",
            user.IsVerified,
            user.Name,
            photo
        );

        return Ok(response);
    }

    private string GenerateJwtToken(string email, string userId, string firebaseUid)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("FirebaseUid", firebaseUid),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.Now.AddHours(8),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private AuthResponse MapToAuthResponse(UsrUser user)
    {
        var token = _jwt.CreateToken(
            user.UserId,
            user.FirebaseUid,
            user.Role,
            user.Email
        );

        var photo = user.UsrUserProfile?.AvatarUrl;

        return new AuthResponse(
            token,
            user.UserId,
            user.Role ?? "USER",
            user.Status ?? "PENDING_ONBOARDING",
            user.IsVerified,
            user.Name,
            photo
        );
    }

}