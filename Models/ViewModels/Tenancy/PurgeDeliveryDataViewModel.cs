using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Models.ViewModels.Tenancy;

/// <summary>The preview (always) and, after a POST, what happened.</summary>
public sealed record PurgeDeliveryDataViewModel(DeliveryDataPurgePreview Preview, DeliveryDataPurgeResult? Result);
