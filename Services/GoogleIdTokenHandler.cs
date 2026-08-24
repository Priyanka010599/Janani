// Services/GoogleIdTokenHandler.cs
// Attaches a Google-signed OIDC ID token to every outgoing request, for
// calling the agent Cloud Run service when it's deployed with
// --no-allow-unauthenticated (see deploy-cloudrun.ps1). Only wired in when
// AGENT_SERVICE_REQUIRES_AUTH=true — local dev calls the agent service
// directly over plain HTTP with no auth required.
using System.Net.Http.Headers;
using Google.Apis.Auth.OAuth2;

namespace Janani.Services;

public class GoogleIdTokenHandler(string audience) : DelegatingHandler
{
    private OidcToken? _token;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // OidcToken caches and refreshes its own underlying token; fetching
        // it once and reusing across requests avoids hitting the metadata
        // server on every call.
        _token ??= await BuildTokenAsync(ct);

        var accessToken = await _token.GetAccessTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await base.SendAsync(request, ct);
    }

    private async Task<OidcToken> BuildTokenAsync(CancellationToken ct)
    {
        var credential = await GoogleCredential.GetApplicationDefaultAsync(ct);
        return await credential.GetOidcTokenAsync(OidcTokenOptions.FromTargetAudience(audience), ct);
    }
}
