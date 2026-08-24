// Services/UserProfileCache.cs
// UserProfile is a single-row table read fresh on nearly every page/agent-context
// build. Since it changes rarely (onboarding, or an edit via Profile.razor),
// cache it once per circuit instead of round-tripping to the DB on every
// navigation/interaction — Profile.razor calls Invalidate() after a save so
// the rest of the circuit picks up the change on its next GetAsync().
//
// Uses its own IDbContextFactory-created DbContext instead of an injected
// shared one, deliberately: NavMenu calls GetAsync() on every page, at the
// same time the routed page runs its own OnInitializedAsync with its own
// injected AppDbContext. Blazor Server starts sibling components' async
// initialization concurrently within a render batch, so two queries sharing
// one scoped DbContext instance throw "A second operation was started on
// this context instance before a previous operation completed" — not just
// between two GetAsync() callers (fixed once by memoizing the in-flight
// Task), but between GetAsync() and *any* unrelated query the page happens
// to run at the same time. A factory-created instance is fully independent
// of whatever DbContext the page injects, so it can never race with it.

using Janani.Data;
using Janani.Models;
using Microsoft.EntityFrameworkCore;

namespace Janani.Services;

public class UserProfileCache(IDbContextFactory<AppDbContext> dbFactory, CurrentUserService currentUser)
{
    private UserProfile? _profile;
    private Task<UserProfile?>? _pendingFetch;

    public Task<UserProfile?> GetAsync()
    {
        if (_profile != null) return Task.FromResult<UserProfile?>(_profile);
        return _pendingFetch ??= FetchAndCacheAsync();
    }

    private async Task<UserProfile?> FetchAndCacheAsync()
    {
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        _profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        return _profile;
    }

    public void Invalidate()
    {
        _profile = null;
        _pendingFetch = null;
    }
}
