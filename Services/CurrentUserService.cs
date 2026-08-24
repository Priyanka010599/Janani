// Services/CurrentUserService.cs
// Resolves "who's logged in" from the auth cookie's ClaimsPrincipal. Reads
// AuthenticationStateProvider rather than HttpContext — HttpContext isn't
// reliably available once a Blazor Server circuit is interactive, but
// AuthenticationStateProvider is the supported way to get auth state inside
// a live circuit. Scoped + cached per instance, same pattern as
// UserProfileCache (a DI scope is one circuit in Blazor Server).

using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Janani.Services;

public class CurrentUserService(AuthenticationStateProvider authStateProvider)
{
    private int? _userId;

    public async Task<int> GetUserIdAsync()
    {
        if (_userId.HasValue) return _userId.Value;

        var state = await authStateProvider.GetAuthenticationStateAsync();
        var claim = state.User.FindFirst(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException(
                "No authenticated user found — this page should be behind the default auth FallbackPolicy.");

        return (_userId = int.Parse(claim.Value)).Value;
    }
}
