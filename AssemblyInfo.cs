using System.Runtime.CompilerServices;

// Lets ProgrammePulse.Tests exercise TotpAuthenticator's internal
// ComputeCode directly instead of brute-forcing a 6-digit code through the
// public ValidateCode surface just to get a known-good value for a test.
[assembly: InternalsVisibleTo("ProgrammePulse.Tests")]
