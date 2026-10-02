namespace ProgrammePulse.Models.Branding;

public sealed record BrandingValidationResult
{
    public required bool IsValid { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public static BrandingValidationResult Success(IReadOnlyList<string>? warnings = null) =>
        new() { IsValid = true, Errors = [], Warnings = warnings ?? [] };

    public static BrandingValidationResult Failure(IReadOnlyList<string> errors, IReadOnlyList<string>? warnings = null) =>
        new() { IsValid = false, Errors = errors, Warnings = warnings ?? [] };
}
