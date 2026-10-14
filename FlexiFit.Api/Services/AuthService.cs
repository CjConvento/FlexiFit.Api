using FirebaseAdmin.Auth;
using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Mappers;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

public class AuthService
{
    private readonly FlexiFitDbContext _db;
    private readonly FirebaseTokenVerifier _firebase;
    private readonly DeviceTokenService _deviceTokenService;
    private readonly JwtService _jwt;

    public AuthService(
        FlexiFitDbContext db,
        FirebaseTokenVerifier firebase,
        DeviceTokenService deviceTokenService,
        JwtService jwt)
    {
        _db = db;
        _firebase = firebase;
        _deviceTokenService = deviceTokenService;
        _jwt = jwt;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FirebaseIdToken))
            throw new ArgumentException("FirebaseIdToken is required.");

        var decoded = await _firebase.VerifyAsync(req.FirebaseIdToken);
        var firebaseUid = decoded.Uid;
        
        decoded.Claims.TryGetValue("email", out var emailObj);
        var email = emailObj?.ToString();
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email not found in Firebase token.");

        var provider = string.IsNullOrWhiteSpace(req.AuthProvider) ? "EMAIL" : req.AuthProvider.ToUpper();
        var now = DateTime.UtcNow;

        // 1. Check existing user
        var existingUser = await _db.UsrUsers
            .Include(u => u.UsrUserProfile)
            .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid || u.Email == email);

        if (existingUser != null)
        {
            existingUser.FirebaseUid = firebaseUid;
            existingUser.AuthProvider = provider;
            existingUser.IsVerified = true;
            existingUser.UpdatedAt = now;
            await _db.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(req.FcmToken))
                await _deviceTokenService.UpsertAsync(existingUser.UserId, req.FcmToken, "android");

            // I-generate muna ang token bago i-pass sa Mapper
            var existingToken = _jwt.CreateToken(
                existingUser.UserId!, 
                existingUser.FirebaseUid!, 
                existingUser.Role!, 
                existingUser.Email!
            );

            return AuthMapper.ToAuthResponse(existingUser, existingToken);
        }

        // 2. Check username availability
        if (await _db.UsrUsers.AnyAsync(u => u.Username == req.Username))
            throw new InvalidOperationException("Username is already taken.");

        // 3. Create New User
        var user = new UsrUser
        {
            FirebaseUid = firebaseUid,
            Email = email,
            Name = req.Name ?? "FlexiFit User",
            Username = req.Username ?? "user_" + Guid.NewGuid().ToString("N")[..7],
            IsVerified = true,
            Role = "USER",
            Status = "PENDING_ONBOARDING",
            AuthProvider = provider,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.UsrUsers.Add(user);
        await _db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(req.FcmToken))
            await _deviceTokenService.UpsertAsync(user.UserId, req.FcmToken, "android");

        var token = _jwt.CreateToken(user.UserId!, user.FirebaseUid!, user.Role!, user.Email!);

        return AuthMapper.ToAuthResponse(user, token);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req)
    {
        var decoded = await _firebase.VerifyAsync(req.FirebaseIdToken);
        
        var user = await _db.UsrUsers
            .Include(u => u.UsrUserProfile)
            .FirstOrDefaultAsync(u => u.FirebaseUid == decoded.Uid);

        if (user == null)
            throw new KeyNotFoundException("User not registered. Please sign up first.");

        if (!string.IsNullOrWhiteSpace(req.FcmToken))
            await _deviceTokenService.UpsertAsync(user.UserId, req.FcmToken, "android");

        var token = _jwt.CreateToken(user.UserId!, user.FirebaseUid!, user.Role!, user.Email!);

        return AuthMapper.ToAuthResponse(user, token);
    }

    public async Task<AuthResponse> AdminLoginAsync(AdminLoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            throw new ArgumentException("Username and password are required.");

        var user = await _db.UsrUsers
            .Include(u => u.UsrUserProfile)
            .FirstOrDefaultAsync(u => u.Username == req.Username);

        // Generic error for security (prevents username enumeration)
        if (user == null || string.IsNullOrEmpty(user.PasswordHash) || user.Role?.ToUpper() != "ADMIN")
            throw new UnauthorizedAccessException("Invalid credentials.");

        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid credentials.");

        var token = _jwt.CreateToken(user.UserId!, user.FirebaseUid!, user.Role!, user.Email!);

        return AuthMapper.ToAuthResponse(user, token);
    }

    public async Task<AuthResponse> RefreshTokenAsync(string firebaseIdToken)
    {
        if (string.IsNullOrEmpty(firebaseIdToken))
            throw new ArgumentException("Firebase token is required.");

        var decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(firebaseIdToken);
        
        var user = await _db.UsrUsers
            .Include(u => u.UsrUserProfile)
            .FirstOrDefaultAsync(u => u.FirebaseUid == decodedToken.Uid)
            ?? throw new KeyNotFoundException("User not found. Please register first.");

        var token = _jwt.CreateToken(user.UserId!, user.FirebaseUid!, user.Role!, user.Email!);

        return AuthMapper.ToAuthResponse(user, token);
    }
}