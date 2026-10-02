using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Services.Staff;

/// <summary>
/// Where a member lands after signing in: the first page their role gets
/// value from.
///
/// Resolved on the request <em>after</em> sign-in (<c>GET /staffops/home</c>),
/// not inside the login POST — the new member cookie is only issued on that
/// response, so a capability check in the same request still sees the
/// anonymous caller.
///
/// Order matters and is deliberate:
/// <list type="number">
/// <item>Anyone who can read delivery reporting (Admin, Team Lead, Board,
/// Analyst) lands on the Review, the Evidence Check: the answer the product
/// is sold on.</item>
/// <item>Anyone else who can see the portfolio lands on the Programme Overview.</item>
/// <item>Everyone else with a tenant role lands on their own work, but only
/// while staff self-service is switched on; with it benched, they get the
/// start page that says nothing is switched on for them.</item>
/// <item>A Platform Admin holds only <see cref="Capability.ManagePlatform"/>,
/// so every tenant page refuses them.</item>
/// </list>
/// Each target is gated on the same capability its controller enforces, so
/// this can never send someone to a page that refuses them. Whether
/// self-service is switched on is passed in, because the module switch
/// belongs to the Tenancy area and this one must not reach into it.
/// </summary>
public static class StaffLandingPage
{
    public const string ResolverPath = "/staffops/home";
    public const string Review = "/staffops/programme/evidence-check";
    public const string ProgrammeOverview = "/staffops/programme";
    public const string MyWork = "/staffops/my-work";
    public const string Start = "/staffops/start";
    public const string PlatformConsole = "/staffops/platform/tenants";
    public const string Login = "/staffops/account/login";

    public static async Task<string> ResolveAsync(IStaffAuthorizationService authorization, bool selfServiceOn)
    {
        if (await authorization.HasAsync(Capability.ViewDeliveryReporting)) return Review;
        if (await authorization.HasAsync(Capability.ViewPortfolio)) return ProgrammeOverview;
        if (await authorization.HasAsync(Capability.ManagePlatform)) return PlatformConsole;
        if (await authorization.HasAsync(Capability.ViewOwnWork)) return selfServiceOn ? MyWork : Start;
        return Login;
    }
}
