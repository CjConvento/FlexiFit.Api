using FlexiFit.Api.Entities;

namespace FlexiFit.Api.Services;

public interface IUserService
{
    Task<UsrUser?> GetByFirebaseUidAsync(string firebaseUid);
}