using System.Text.Json;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Staff;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.Security;

public sealed class MfaRepository(IScopeProvider scopeProvider) : IMfaRepository
{
    public async Task<MemberMfaCredential?> GetForMemberAsync(int memberId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<MemberMfaDto>(
            Sql.Builder.Where("memberId = @0", memberId));
        return dto is null ? null : Map(dto);
    }

    public async Task<MemberMfaCredential> StartEnrollmentAsync(int memberId, string protectedTotpSecret, DateTime nowUtc)
    {
        if (!protectedTotpSecret.StartsWith(MfaSecretProtection.Prefix, StringComparison.Ordinal))
            throw new ArgumentException("Only protected MFA secrets may be stored.", nameof(protectedTotpSecret));
        using var scope = scopeProvider.CreateScope();
        var existing = await scope.Database.FirstOrDefaultAsync<MemberMfaDto>(
            Sql.Builder.Where("memberId = @0", memberId));

        if (existing is not null)
        {
            scope.Complete();
            return Map(existing);
        }

        var dto = new MemberMfaDto
        {
            MemberId = memberId,
            TotpSecret = string.Empty,
            ProtectedTotpSecret = protectedTotpSecret,
            CredentialVersion = Guid.NewGuid(),
            Enabled = false,
            CreatedAtUtc = nowUtc
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task<bool> TryAcceptTotpAsync(MemberMfaCredential expected, long step, DateTime nowUtc, IReadOnlyList<string>? enrollmentRecoveryHashes = null)
    {
        using var scope = scopeProvider.CreateScope();
        if (!expected.Enabled && enrollmentRecoveryHashes is null) return false;
        var recoveryJson = enrollmentRecoveryHashes is null ? null
            : JsonSerializer.Serialize(enrollmentRecoveryHashes.Select(h => new MfaRecoveryCode(h, null)));
        var updated = await scope.Database.ExecuteAsync(new Sql(
            $"UPDATE {MemberMfaDto.TableName} SET lastAcceptedTimeStep=@0, enabled=@1, enabledAtUtc=COALESCE(enabledAtUtc,@2), recoveryCodesJson=COALESCE(@3,recoveryCodesJson) "
            + "WHERE memberId=@4 AND credentialVersion=@5 AND protectedTotpSecret=@6 AND enabled=@7 AND (lastAcceptedTimeStep IS NULL OR lastAcceptedTimeStep<@0)",
            step, true, nowUtc, recoveryJson, expected.MemberId, expected.CredentialVersion, expected.ProtectedTotpSecret, expected.Enabled));
        scope.Complete();
        return updated == 1;
    }

    public async Task SetRecoveryCodeHashesAsync(int memberId, IReadOnlyList<string> hashes)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<MemberMfaDto>(
            Sql.Builder.Where("memberId = @0", memberId))
            ?? throw new InvalidOperationException($"No MFA enrollment started for member {memberId}.");

        dto.RecoveryCodesJson = JsonSerializer.Serialize(hashes.Select(h => new MfaRecoveryCode(h, null)));

        // Never write a stale DTO over a concurrently accepted TOTP time step.
        await scope.Database.ExecuteAsync(
            $"UPDATE {MemberMfaDto.TableName} SET recoveryCodesJson=@0 WHERE memberId=@1 AND credentialVersion=@2",
            dto.RecoveryCodesJson, memberId, dto.CredentialVersion);
        scope.Complete();
    }

    /// <summary>Marks the first unused code matching this hash as used. Returns the number of codes still unused after, or null if the hash didn't match any unused code (wrong or already-redeemed).</summary>
    public async Task<int?> RedeemRecoveryCodeAsync(int memberId, Func<string, bool> matches, DateTime usedAtUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<MemberMfaDto>(
            Sql.Builder.Where("memberId = @0", memberId));

        var codes = ParseRecoveryCodes(dto?.RecoveryCodesJson);
        var index = codes.FindIndex(c => c.UsedAtUtc is null && matches(c.Hash));
        if (dto is null || index < 0)
        {
            scope.Complete();
            return null;
        }

        codes[index] = codes[index] with { UsedAtUtc = usedAtUtc };
        // Compare-and-swap prevents simultaneous redemptions from accepting the same code twice.
        var updated = await scope.Database.ExecuteAsync(
            $"UPDATE {MemberMfaDto.TableName} SET recoveryCodesJson = @0 WHERE memberId = @1 AND recoveryCodesJson = @2 AND enabled = @3 AND credentialVersion = @4",
            JsonSerializer.Serialize(codes), memberId, dto.RecoveryCodesJson, true, dto.CredentialVersion);
        scope.Complete();
        return updated == 1 ? codes.Count(c => c.UsedAtUtc is null) : null;
    }

    /// <summary>Deletes the enrollment entirely so the member is re-prompted to set up MFA from scratch on next login — the admin-to-admin reset path (StaffAdminController) for a member who lost their device and has no unused recovery codes.</summary>

    public async Task<DateTime?> GetLockedUntilAsync(int memberId, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var lockedUntil = await scope.Database.ExecuteScalarAsync<DateTime?>(
            $"SELECT lockedUntilUtc FROM {MemberMfaDto.TableName} WHERE memberId=@0", memberId);
        return lockedUntil is { } until && until > nowUtc ? until : null;
    }

    public async Task<bool> TryBeginAttemptAsync(int memberId, DateTime nowUtc, Func<int, DateTime?> lockUntilAfter)
    {
        using var scope = scopeProvider.CreateScope();
        // UPDLOCK holds the row until this scope commits, so a parallel attempt
        // reads the count only after this one has written it.
        var row = await scope.Database.FirstOrDefaultAsync<LockoutRow>(
            $"SELECT failedAttempts AS FailedAttempts, lockedUntilUtc AS LockedUntilUtc FROM {MemberMfaDto.TableName} WITH (UPDLOCK, HOLDLOCK) WHERE memberId=@0",
            memberId);
        if (row is null || row.LockedUntilUtc > nowUtc)
        {
            scope.Complete();
            return false;
        }

        var attempts = row.FailedAttempts + 1;
        await scope.Database.ExecuteAsync(
            $"UPDATE {MemberMfaDto.TableName} SET failedAttempts=@0, lockedUntilUtc=COALESCE(@1, lockedUntilUtc) WHERE memberId=@2",
            attempts, lockUntilAfter(attempts), memberId);
        scope.Complete();
        return true;
    }

    public async Task ClearFailuresAsync(int memberId)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"UPDATE {MemberMfaDto.TableName} SET failedAttempts = 0, lockedUntilUtc = NULL WHERE memberId=@0 AND (failedAttempts <> 0 OR lockedUntilUtc IS NOT NULL)", memberId);
        scope.Complete();
    }

    private sealed class LockoutRow
    {
        public int FailedAttempts { get; set; }
        public DateTime? LockedUntilUtc { get; set; }
    }

    public async Task ResetAsync(int memberId)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync($"DELETE FROM {MemberMfaChallengeDto.TableName} WHERE memberId = @0", memberId);
        await scope.Database.ExecuteAsync(
            $"DELETE FROM {MemberMfaDto.TableName} WHERE memberId = @0", memberId);
        scope.Complete();
    }

    private static List<MfaRecoveryCode> ParseRecoveryCodes(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<MfaRecoveryCode>>(json) ?? [];

    private static MemberMfaCredential Map(MemberMfaDto dto) => new()
    {
        MemberId = dto.MemberId,
        ProtectedTotpSecret = dto.ProtectedTotpSecret ?? throw new InvalidOperationException("MFA secret migration is required."),
        CredentialVersion = dto.CredentialVersion ?? throw new InvalidOperationException("MFA credential version is missing."),
        LastAcceptedTimeStep = dto.LastAcceptedTimeStep,
        Enabled = dto.Enabled,
        EnabledAtUtc = dto.EnabledAtUtc,
        CreatedAtUtc = dto.CreatedAtUtc,
        RecoveryCodes = ParseRecoveryCodes(dto.RecoveryCodesJson)
    };
}
