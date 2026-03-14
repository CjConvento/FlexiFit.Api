namespace FlexiFit.Api.Dtos;

// Para sa pag-create ng bagong account
public record RegisterRequest(
    string FirebaseIdToken,
    string? Name,
    string? Username,
    string? FcmToken,          // Para sa push notifications
    string? AuthProvider // "GOOGLE" or "EMAIL"
);

// Para sa pag-pasok ng existing user
public record LoginRequest(
    string FirebaseIdToken,
    string? FcmToken          // I-update natin ito tuwing login para laging active
);

// Ang data na matatanggap ng Android phone pagkatapos ng Auth
public record AuthResponse(
    string Token,             // Ang JWT Token para sa Authorization
    int UserId,               // Unique ID ni user sa DB mo
    string Role,              // "User" or "Admin"
    string Status,            // "PENDING_ONBOARDING" o "ACTIVE"
    bool IsVerified,          // Kung na-verify na ang email/phone
    string? Name,             // Isama na natin para pang-display sa Header ng App
    string? PhotoUrl = null          // Para sa profile picture ni user
);