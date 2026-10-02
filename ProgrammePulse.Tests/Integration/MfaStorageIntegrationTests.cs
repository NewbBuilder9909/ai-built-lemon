using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Migrations.StaffOps;
using ProgrammePulse.Services.Security;
using ProgrammePulse.Services.Staff;
using Umbraco.Cms.Infrastructure.Scoping;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class MfaStorageIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "no MFA secret-storage or concurrency behaviour was exercised against a real database.";

    // Generated per run: no secret-looking literal in source (Aikido: generic API key).
    private static readonly string Secret = TotpAuthenticator.GenerateSecret();
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 6, 0, 0, TimeSpan.Zero);
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private static string Code(DateTimeOffset now) => TotpAuthenticator.ComputeCode(TotpAuthenticator.Base32Decode(Secret), now.ToUnixTimeSeconds() / 30);
    private static int MemberId() => System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000000, int.MaxValue);

    private static MfaVerificationService Verifier(IServiceProvider services, DateTimeOffset now) => new(
        services.GetRequiredService<IMfaRepository>(), services.GetRequiredService<MfaSecretProtection>(),
        services.GetRequiredService<IDataProtectionProvider>(), new Clock(now));

    [Fact]
    public async Task Enrollment_stores_only_ciphertext_and_never_returns_an_enabled_seed()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var repository = services.GetRequiredService<IMfaRepository>();
        var id = MemberId();
        var credential = await repository.StartEnrollmentAsync(id, services.GetRequiredService<MfaSecretProtection>().Protect(id, Secret), Now.UtcDateTime);
        var verifier = Verifier(services, Now);
        try
        {
            using var db = services.GetRequiredService<IScopeProvider>().CreateScope(autoComplete: true);
            var stored = await db.Database.FirstAsync<MemberMfaDto>(Sql.Builder.Where("memberId=@0", id));
            Assert.Equal(string.Empty, stored.TotpSecret);
            Assert.DoesNotContain(Secret, stored.ProtectedTotpSecret!);
            Assert.Equal(Secret, await verifier.GetEnrollmentSecretAsync(id, credential.CredentialVersion));
            Assert.NotNull(await verifier.ConfirmEnrollmentAsync(id, credential.CredentialVersion, Code(Now)));
            Assert.Null(await verifier.GetEnrollmentSecretAsync(id, credential.CredentialVersion));
            Assert.False(await verifier.VerifyAsync(id, credential.CredentialVersion, Code(Now)));
        }
        finally { await repository.ResetAsync(id); }
    }

    [Fact]
    public async Task Same_or_older_steps_are_rejected_and_newer_steps_are_accepted()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var repository = services.GetRequiredService<IMfaRepository>();
        var id = MemberId();
        var credential = await repository.StartEnrollmentAsync(id, services.GetRequiredService<MfaSecretProtection>().Protect(id, Secret), Now.UtcDateTime);
        try
        {
            Assert.NotNull(await Verifier(services, Now).ConfirmEnrollmentAsync(id, credential.CredentialVersion, Code(Now)));
            Assert.False(await Verifier(services, Now).VerifyAsync(id, credential.CredentialVersion, Code(Now)));
            Assert.False(await Verifier(services, Now).VerifyAsync(id, credential.CredentialVersion, Code(Now.AddSeconds(-30))));
            Assert.True(await Verifier(services, Now.AddSeconds(30)).VerifyAsync(id, credential.CredentialVersion, Code(Now.AddSeconds(30))));
            Assert.False(await Verifier(services, Now.AddSeconds(30)).VerifyAsync(id, credential.CredentialVersion, Code(Now)));
            Assert.Equal(Now.AddSeconds(30).ToUnixTimeSeconds() / 30, (await repository.GetForMemberAsync(id))!.LastAcceptedTimeStep);
        }
        finally { await repository.ResetAsync(id); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_verification_or_enrollment_accepts_exactly_one_request(bool enrollment)
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        using var setup = factory.Services.CreateScope();
        var repository = setup.ServiceProvider.GetRequiredService<IMfaRepository>();
        var id = MemberId();
        var credential = await repository.StartEnrollmentAsync(id, setup.ServiceProvider.GetRequiredService<MfaSecretProtection>().Protect(id, Secret), Now.UtcDateTime);
        try
        {
            if (!enrollment) Assert.NotNull(await Verifier(setup.ServiceProvider, Now.AddSeconds(-30)).ConfirmEnrollmentAsync(id, credential.CredentialVersion, Code(Now.AddSeconds(-30))));
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            {
                await gate.Task;
                using var attempt = factory.Services.CreateScope();
                var verifier = Verifier(attempt.ServiceProvider, Now);
                return enrollment
                    ? await verifier.ConfirmEnrollmentAsync(id, credential.CredentialVersion, Code(Now)) is not null
                    : await verifier.VerifyAsync(id, credential.CredentialVersion, Code(Now));
            })).ToArray();
            gate.SetResult();
            Assert.Single(await Task.WhenAll(tasks), accepted => accepted);
        }
        finally { await repository.ResetAsync(id); }
    }

    [Fact]
    public async Task Migration_encrypts_legacy_rows_blanks_plaintext_and_is_idempotent()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        using var scope = factory.Services.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<IScopeProvider>().CreateScope();
        var id = MemberId();
        await db.Database.InsertAsync(new MemberMfaDto { MemberId = id, TotpSecret = Secret, Enabled = true, CreatedAtUtc = Now.UtcDateTime });
        var protector = scope.ServiceProvider.GetRequiredService<MfaSecretProtection>();
        await ProtectMfaSecrets.EncryptLegacyRowsAsync(db.Database, protector);
        var migrated = await db.Database.FirstAsync<MemberMfaDto>(Sql.Builder.Where("memberId=@0", id));
        Assert.Empty(migrated.TotpSecret);
        Assert.NotNull(migrated.ProtectedTotpSecret);
        Assert.DoesNotContain(Secret, migrated.ProtectedTotpSecret);
        Assert.True(migrated.Enabled);
        Assert.True(await Verifier(scope.ServiceProvider, Now).VerifyAsync(id, migrated.CredentialVersion!.Value, Code(Now)));
        await ProtectMfaSecrets.EncryptLegacyRowsAsync(db.Database, protector);
        var repeated = await db.Database.FirstAsync<MemberMfaDto>(Sql.Builder.Where("memberId=@0", id));
        Assert.Equal(migrated.ProtectedTotpSecret, repeated.ProtectedTotpSecret);
        Assert.Equal(migrated.CredentialVersion, repeated.CredentialVersion);
        Assert.Equal(Now.ToUnixTimeSeconds() / 30, repeated.LastAcceptedTimeStep);
        // Deliberately roll back this legacy-row fixture and all its updates.
    }

    [Fact]
    public async Task Replaced_credential_version_cannot_reveal_or_verify_the_new_enrollment()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMfaRepository>();
        var verifier = Verifier(scope.ServiceProvider, Now);
        var id = MemberId();
        var first = await verifier.EnsureEnrollmentAsync(id);
        await repository.ResetAsync(id);
        var second = await verifier.EnsureEnrollmentAsync(id);
        try
        {
            Assert.NotEqual(first.CredentialVersion, second.CredentialVersion);
            Assert.Null(await verifier.GetEnrollmentSecretAsync(id, first.CredentialVersion));
            Assert.Null(await verifier.ConfirmEnrollmentAsync(id, first.CredentialVersion, Code(Now)));
        }
        finally { await repository.ResetAsync(id); }
    }

    [Fact]
    public async Task Wrong_codes_across_challenges_lock_mfa_and_a_locked_member_is_refused_even_with_the_right_code()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var repository = services.GetRequiredService<IMfaRepository>();
        var id = MemberId();
        var credential = await repository.StartEnrollmentAsync(id, services.GetRequiredService<MfaSecretProtection>().Protect(id, Secret), Now.UtcDateTime);
        try
        {
            Assert.NotNull(await Verifier(services, Now).ConfirmEnrollmentAsync(id, credential.CredentialVersion, Code(Now)));

            // Nine wrong codes (mixed TOTP and recovery) leave the member able to sign in.
            var later = Now.AddMinutes(1);
            for (var i = 0; i < MfaVerificationService.MaxFailures - 1; i++)
            {
                if (i % 2 == 0) Assert.False(await Verifier(services, later).VerifyAsync(id, credential.CredentialVersion, "000000"));
                else Assert.Null(await Verifier(services, later).RedeemRecoveryCodeAsync(id, "not-a-code"));
            }
            Assert.Null(await Verifier(services, later).GetLockedUntilAsync(id));

            // The tenth locks, and then even the right code is refused.
            Assert.False(await Verifier(services, later).VerifyAsync(id, credential.CredentialVersion, "000000"));
            Assert.NotNull(await Verifier(services, later).GetLockedUntilAsync(id));
            var stillLocked = later.AddMinutes(5);
            Assert.False(await Verifier(services, stillLocked).VerifyAsync(id, credential.CredentialVersion, Code(stillLocked)));

            // After the lock the right code works, and a success clears the count.
            var unlocked = later + MfaVerificationService.LockDuration + TimeSpan.FromMinutes(1);
            Assert.Null(await Verifier(services, unlocked).GetLockedUntilAsync(id));
            Assert.True(await Verifier(services, unlocked).VerifyAsync(id, credential.CredentialVersion, Code(unlocked)));
            using var db = services.GetRequiredService<IScopeProvider>().CreateScope(autoComplete: true);
            Assert.Equal(0, await db.Database.ExecuteScalarAsync<int>($"SELECT failedAttempts FROM {MemberMfaDto.TableName} WHERE memberId=@0", id));
        }
        finally { await repository.ResetAsync(id); }
    }

    [Fact]
    public async Task Parallel_wrong_codes_cannot_outrun_the_lock()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        var (id, credential) = await EnrolAsync();
        try
        {
            // Checking the lock and counting the failure separately let every
            // request already in flight through. Each attempt now spends its
            // place first, so forty at once still get exactly ten guesses.
            var later = Now.AddMinutes(1);
            var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(async () =>
            {
                using var scope = factory.Services.CreateScope();
                return await Verifier(scope.ServiceProvider, later).VerifyAsync(id, credential.CredentialVersion, "000000");
            })));

            Assert.All(results, Assert.False);
            var (failures, lockedUntil) = await LockoutAsync(id);
            Assert.Equal(MfaVerificationService.MaxFailures, failures);
            Assert.Equal(later.UtcDateTime + MfaVerificationService.LockDuration, lockedUntil);
        }
        finally { await Reset(id); }
    }

    [Fact]
    public async Task Locks_escalate_and_the_hundredth_wrong_code_holds_until_an_admin_reset()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;
        var (id, credential) = await EnrolAsync();
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;

                // Waiting out a lock no longer restores a fresh budget: the
                // twentieth consecutive failure locks for twice as long.
                await SetFailuresAsync(id, 19);
                var later = Now.AddMinutes(1);
                Assert.False(await Verifier(services, later).VerifyAsync(id, credential.CredentialVersion, "000000"));
                Assert.Equal((20, (DateTime?)(later.UtcDateTime + TimeSpan.FromMinutes(30))), await LockoutAsync(id));

                await SetFailuresAsync(id, 99);
                var afterLock = later.AddHours(1);
                Assert.Null(await Verifier(services, afterLock).RedeemRecoveryCodeAsync(id, "not-a-code"));
                Assert.Equal(MfaVerificationService.LockedUntilReset, await Verifier(services, afterLock).GetLockedUntilAsync(id));

                var nextYear = afterLock.AddYears(1);
                Assert.False(await Verifier(services, nextYear).VerifyAsync(id, credential.CredentialVersion, Code(nextYear)));
            }

            // An Admin's reset is the way out: it removes the enrollment, lock included.
            await Reset(id);
            using var after = factory.Services.CreateScope();
            Assert.Null(await after.ServiceProvider.GetRequiredService<IMfaRepository>().GetLockedUntilAsync(id, Now.UtcDateTime));
        }
        finally { await Reset(id); }
    }

    private async Task<(int Id, ProgrammePulse.Models.Staff.MemberMfaCredential Credential)> EnrolAsync()
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var id = MemberId();
        var credential = await services.GetRequiredService<IMfaRepository>()
            .StartEnrollmentAsync(id, services.GetRequiredService<MfaSecretProtection>().Protect(id, Secret), Now.UtcDateTime);
        Assert.NotNull(await Verifier(services, Now).ConfirmEnrollmentAsync(id, credential.CredentialVersion, Code(Now)));
        return (id, credential);
    }

    private async Task SetFailuresAsync(int memberId, int failures)
    {
        using var scope = factory.Services.GetRequiredService<IScopeProvider>().CreateScope();
        await scope.Database.ExecuteAsync(
            $"UPDATE {MemberMfaDto.TableName} SET failedAttempts=@0, lockedUntilUtc=NULL WHERE memberId=@1", failures, memberId);
        scope.Complete();
    }

    private async Task<(int Failures, DateTime? LockedUntil)> LockoutAsync(int memberId)
    {
        using var scope = factory.Services.GetRequiredService<IScopeProvider>().CreateScope(autoComplete: true);
        var failures = await scope.Database.ExecuteScalarAsync<int>($"SELECT failedAttempts FROM {MemberMfaDto.TableName} WHERE memberId=@0", memberId);
        var until = await scope.Database.ExecuteScalarAsync<DateTime?>($"SELECT lockedUntilUtc FROM {MemberMfaDto.TableName} WHERE memberId=@0", memberId);
        return (failures, until);
    }

    private async Task Reset(int memberId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMfaRepository>().ResetAsync(memberId);
    }
}
