using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;

namespace FlexiFit.Api.Mappers;

public static class AuthMapper
{
    /// <summary>
    /// Maps a UsrUser entity and a generated JWT token to an AuthResponse DTO.
    /// </summary>
    public static AuthResponse ToAuthResponse(UsrUser user, string token)
    {
        // Kunin ang AvatarUrl mula sa related Profile (kung meron)
        var photo = user.UsrUserProfile?.AvatarUrl;
        
        return new AuthResponse(
            Token: token,
            UserId: user.UserId,
            Role: user.Role ?? "USER",
            Status: user.Status ?? "PENDING_ONBOARDING",
            IsVerified: user.IsVerified,
            Name: user.Name,
            PhotoUrl: photo
        );
    }
}