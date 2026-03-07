namespace FlexiFit.Api.Dtos;

public record RegisterRequest(
    string FirebaseIdToken,
    string? Name,
    string? Username,
    string? Address,
    string? FcmToken          // NEW
);

public record LoginRequest(
    string FirebaseIdToken,
    string? FcmToken          // NEW (optional but recommended)
);

public record AuthResponse(
    string Token,
    int UserId,
    string Role,
    string Status,
    bool IsVerified
);