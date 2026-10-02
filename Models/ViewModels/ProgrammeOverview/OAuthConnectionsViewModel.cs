namespace ProgrammePulse.Models.ViewModels.ProgrammeOverview;

/// <summary>The Jira and Tempo connection page: a person-readable state for each connection.</summary>
public sealed record OAuthConnectionsViewModel(string JiraStatus, string TempoStatus);
