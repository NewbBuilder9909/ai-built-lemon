namespace ProgrammePulse.Services.BrandingOps;

/// <summary>
/// Raised by IBrandingAssetStorageService when an upload fails validation
/// (wrong content type, too large, or fails real image decoding). The
/// controller catches this and surfaces the message to the admin form rather
/// than letting it bubble as a 500.
/// </summary>
public sealed class BrandingAssetValidationException(string message) : Exception(message);
