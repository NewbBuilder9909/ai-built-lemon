namespace ProgrammePulse.Models.Erp;

public sealed record ValidationMessage(ValidationSeverity Severity, string Message);

/// <summary>
/// Outcome of validating a single RawErpRecord. A record can have warnings and
/// still be considered valid; any Error-severity message makes it invalid.
/// </summary>
public sealed class ValidationResult
{
    public List<ValidationMessage> Messages { get; } = [];

    public bool HasErrors => Messages.Any(m => m.Severity == ValidationSeverity.Error);

    public bool HasWarnings => Messages.Any(m => m.Severity == ValidationSeverity.Warning);

    public bool IsValid => !HasErrors;

    public void AddError(string message) => Messages.Add(new ValidationMessage(ValidationSeverity.Error, message));

    public void AddWarning(string message) => Messages.Add(new ValidationMessage(ValidationSeverity.Warning, message));

    public void AddInfo(string message) => Messages.Add(new ValidationMessage(ValidationSeverity.Info, message));
}
