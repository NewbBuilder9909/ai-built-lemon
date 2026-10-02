using System.Globalization;
using ProgrammePulse.Models.Integrations.HubPlanner.Raw;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Services.Integrations.HubPlanner;

public sealed class HubPlannerMappingService(
    IProgrammeRepository programmeRepository,
    TimeProvider timeProvider) : IHubPlannerMappingService
{
    private const string Source = "HubPlanner";
    private const string RootProgrammeExternalId = "hubplanner-root";

    public Task<Programme> MapRootProgrammeAsync(Guid tenantId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return programmeRepository.UpsertProgrammeAsync(new Programme
        {
            ProgrammeKey = Guid.NewGuid(),
            Name = "Hub Planner",
            ExternalSource = Source,
            ExternalId = RootProgrammeExternalId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, tenantId);
    }

    public Task<Project> MapProjectAsync(HubPlannerProjectDto project, Guid programmeKey, Guid tenantId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return programmeRepository.UpsertProjectAsync(new Project
        {
            ProjectKey = Guid.NewGuid(),
            ProgrammeKey = programmeKey,
            Name = project.Name,
            ExternalSource = Source,
            ExternalId = project.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, tenantId);
    }

    /// <summary>
    /// No lifecycle stage is derived here — a booking whose end date has
    /// passed is a booking that has elapsed, not work that was done. Hours
    /// are copied only when the source's own unit is hours
    /// (state == "STATE_HOURS"); a percentage booking keeps its percentage
    /// and leaves AllocatedHours null, so Gold can report "no hours
    /// recorded" instead of inventing a conversion that hasn't been
    /// verified against a live account.
    /// </summary>
    public Task<PlannedAllocation> MapBookingAsync(HubPlannerBookingDto booking, Guid projectKey, Guid? resolvedStaffKey, Guid tenantId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return programmeRepository.UpsertPlannedAllocationAsync(new PlannedAllocation
        {
            PlannedAllocationKey = Guid.NewGuid(),
            ProjectKey = projectKey,
            StaffKey = resolvedStaffKey,
            Title = ResolveTitle(booking),
            StartUtc = ParseIsoDate(booking.Start),
            EndUtc = ParseIsoDate(booking.End),
            AllocatedHours = booking.State == "STATE_HOURS" ? (decimal?)booking.StateValue : null,
            AllocationPercent = booking.State == "STATE_PERCENTAGE" ? (decimal?)booking.StateValue : null,
            RawType = booking.Type,
            ExternalSource = Source,
            ExternalId = booking.Id,
            ExternalResourceId = string.IsNullOrWhiteSpace(booking.Resource) ? null : booking.Resource,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, tenantId);
    }

    private static string ResolveTitle(HubPlannerBookingDto booking)
    {
        if (!string.IsNullOrWhiteSpace(booking.Title))
        {
            return booking.Title;
        }

        if (!string.IsNullOrWhiteSpace(booking.CategoryName))
        {
            return booking.CategoryName;
        }

        return "Booking";
    }

    /// <summary>
    /// Hub Planner's documented examples show booking start/end without a UTC
    /// offset (e.g. "2018-06-07T09:00") — parsed as UTC rather than assumed
    /// local, so date-window filtering in Gold is approximate near a day
    /// boundary until that's confirmed against a real account.
    /// </summary>
    private static DateTime? ParseIsoDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }
}
