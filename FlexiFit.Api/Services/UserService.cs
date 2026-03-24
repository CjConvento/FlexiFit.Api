using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

public class UserService : IUserService
{
    private readonly FlexiFitDbContext _db;

    public UserService(FlexiFitDbContext db)
    {
        _db = db;
    }

    public async Task<UsrUser?> GetByFirebaseUidAsync(string firebaseUid)
    {
        return await _db.UsrUsers
            .Include(u => u.UsrUserProfile) // para makuha ang AvatarUrl
            .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid);
    }
}