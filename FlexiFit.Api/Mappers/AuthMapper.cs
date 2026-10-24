using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;

namespace FlexiFit.Api.Mappers;

public static class AuthMapper
{
    /// <summary>
    /// Maps a UsrUser entity and a generated JWT token to an AuthResponse DTO.
    /// NULL-SAFE: Handles users with no profile or nullified profile data.
    /// </summary>
    public static AuthResponse ToAuthResponse(UsrUser user, string token)
    {
        // Kunin ang AvatarUrl mula sa related Profile (kung meron)
        var photo = user.UsrUserProfile?.AvatarUrl;

        // Kunin ang Name - may fallback para sa walang profile
        var name = user.Name 
            ?? user.UsrUserProfile?.Name 
            ?? user.Email?.Split('@')[0]  // Fallback: username from email
            ?? "FlexiFit User";            // Final fallback

        return new AuthResponse(
            Token: token,
            UserId: user.UserId,
            Role: user.Role ?? "USER",
            Status: user.Status ?? "PENDING_ONBOARDING",
            IsVerified: user.IsVerified,
            Name: name,
            PhotoUrl: photo
        );
    }
}