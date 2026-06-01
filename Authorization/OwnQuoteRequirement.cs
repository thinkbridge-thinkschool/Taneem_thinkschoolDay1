using Microsoft.AspNetCore.Authorization;
using QuotesApi.Data;
using System.Security.Claims;

namespace QuotesApi.Authorization;

// ── Requirement ───────────────────────────────────────────────────────────
// Marker — no properties needed. The handler does all the work.
public class OwnQuoteRequirement : IAuthorizationRequirement { }

// ── Handler ───────────────────────────────────────────────────────────────
// Checks if the current user owns the quote they're trying to delete.
// Admins can delete any quote.
public class OwnQuoteHandler : AuthorizationHandler<OwnQuoteRequirement>
{
    private readonly IHttpContextAccessor _http;
    private readonly AppDbContext         _db;

    public OwnQuoteHandler(IHttpContextAccessor http, AppDbContext db)
    {
        _http = http;
        _db   = db;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnQuoteRequirement         requirement)
    {
        var httpContext = _http.HttpContext;
        if (httpContext is null)
        {
            context.Fail();
            return;
        }

        // Admins can delete anything
        if (context.User.HasClaim("role", "admin"))
        {
            context.Succeed(requirement);
            return;
        }

        // Get the quote id from the route
        if (!httpContext.Request.RouteValues.TryGetValue("id", out var idValue)
            || !int.TryParse(idValue?.ToString(), out var quoteId))
        {
            context.Fail();
            return;
        }

        // Get the current user's id from the token
        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is null || !int.TryParse(userIdClaim, out var userId))
        {
            context.Fail();
            return;
        }

        // Check the quote exists and belongs to this user
        var quote = await _db.Quotes.FindAsync(quoteId);
        if (quote is null)
        {
            context.Fail();
            return;
        }

        if (quote.CreatedByUserId == userId)
            context.Succeed(requirement);
        else
            context.Fail();
    }
}