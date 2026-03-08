using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

public class DeviceTokenService
{
    private readonly FlexiFitDbContext _db;
    public DeviceTokenService(FlexiFitDbContext db) => _db = db;

    public async Task UpsertAsync(int userId, string fcmToken, string platform = "android")
    {
        var token = (fcmToken ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(token)) return;

        var existing = await _db.UsrDeviceTokens
            .FirstOrDefaultAsync(x => x.FcmToken == token);

        if (existing == null)
        {
            _db.UsrDeviceTokens.Add(new UsrDeviceToken
            {
                UserId = userId,
                FcmToken = token,
                Platform = platform,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.UserId = userId;
            existing.Platform = platform;
            existing.IsActive = true;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }
}