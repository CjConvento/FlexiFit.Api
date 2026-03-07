using FirebaseAdmin.Auth;

namespace FlexiFit.Api.Services;

public class FirebaseTokenVerifier
{
    public Task<FirebaseToken> VerifyAsync(string firebaseIdToken)
    {
        return FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(firebaseIdToken);
    }
}