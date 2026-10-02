using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProgrammePulse.Controllers;

namespace ProgrammePulse.Tests.Architecture;

/// <summary>
/// A sync is a long outbound read against someone else's API, started by one
/// POST. Programme sync and file import were throttled per client, but four
/// later sync buttons (GitHub, Azure DevOps, Freshdesk, Aikido) were not, so a
/// held-down button or a script could queue runs without limit (Aikido:
/// uncontrolled resource consumption). This makes the throttle a build rule
/// for any action whose route has a <c>sync</c> segment.
/// </summary>
public class SyncRateLimitTests
{
    [Fact]
    public void Every_sync_action_is_rate_limited()
    {
        var syncActions = typeof(StaffAccountController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(m => (Controller: t, Action: m)))
            .Where(a => a.Action.GetCustomAttributes<HttpPostAttribute>()
                .Any(p => p.Template?.Split('/').Contains("sync", StringComparer.OrdinalIgnoreCase) == true))
            .ToList();

        Assert.NotEmpty(syncActions);
        var unthrottled = syncActions
            .Where(a => (a.Action.GetCustomAttribute<EnableRateLimitingAttribute>()
                         ?? a.Controller.GetCustomAttribute<EnableRateLimitingAttribute>())?.PolicyName != "sync")
            .Select(a => $"{a.Controller.Name}.{a.Action.Name}")
            .ToList();

        Assert.True(unthrottled.Count == 0, "Sync actions without [EnableRateLimiting(\"sync\")]: " + string.Join(", ", unthrottled));
    }
}
