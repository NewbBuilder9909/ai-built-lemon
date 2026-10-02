using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Services.Staff;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Cms.Web.Common.Security;

namespace ProgrammePulse.Services.Security;

public sealed class MfaChallengeStore(
    IScopeProvider scopeProvider, MemberManager memberManager, IStaffRepository staffRepository,
    IMfaRepository mfaRepository, TimeProvider timeProvider, IConfiguration configuration) : IMfaChallengeStore
{
    public const string SchemeName = "MfaPending";
    public const string BindingCookieName = "ops-mfa-browser";
    private const string ReturnUrlClaimType = "mfa_return_url";
    private const string ChallengeClaimType = "mfa_challenge";
    private const int MaxFailedAttempts = 5;

    public async Task StartAsync(HttpContext context, string memberId, string securityStamp, string? returnUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);
        await ClearAsync(context);
        var credential = await mfaRepository.GetForMemberAsync(int.Parse(memberId))
            ?? throw new InvalidOperationException("MFA enrollment is required before starting a challenge.");
        var binding = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var challenge = new MemberMfaChallengeDto
        {
            ChallengeKey = Guid.NewGuid(), MemberId = int.Parse(memberId), BindingHash = Hash(binding),
            SecurityStamp = securityStamp, CredentialVersion = credential.CredentialVersion,
            ExpiresAtUtc = timeProvider.GetUtcNow().AddMinutes(5).UtcDateTime
        };
        using (var scope = scopeProvider.CreateScope())
        {
            await scope.Database.ExecuteAsync($"DELETE FROM {MemberMfaChallengeDto.TableName} WHERE expiresAtUtc <= @0", timeProvider.GetUtcNow().UtcDateTime);
            await scope.Database.InsertAsync(challenge);
            scope.Complete();
        }
        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, memberId));
        identity.AddClaim(new Claim(ChallengeClaimType, challenge.ChallengeKey.ToString("D")));
        if (!string.IsNullOrWhiteSpace(returnUrl)) identity.AddClaim(new Claim(ReturnUrlClaimType, returnUrl));
        context.Response.Cookies.Append(BindingCookieName, binding, CookieOptions());
        await context.SignInAsync(SchemeName, new ClaimsPrincipal(identity));
    }

    public async Task<(string MemberId, string? ReturnUrl, Guid CredentialVersion)?> GetAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(SchemeName);
        var challenge = await ValidateAsync(context, result);
        if (challenge is null) return null;
        return (challenge.MemberId.ToString(), result.Principal?.FindFirstValue(ReturnUrlClaimType), challenge.CredentialVersion);
    }

    public async Task<bool> TryConsumeAsync(HttpContext context, string memberId)
    {
        var result = await context.AuthenticateAsync(SchemeName);
        var challenge = await ValidateAsync(context, result);
        if (challenge is null || challenge.MemberId.ToString() != memberId) return false;
        using var scope = scopeProvider.CreateScope();
        var count = await scope.Database.ExecuteAsync(
            $"DELETE FROM {MemberMfaChallengeDto.TableName} WHERE challengeKey=@0 AND expiresAtUtc>@1",
            challenge.ChallengeKey, timeProvider.GetUtcNow().UtcDateTime);
        scope.Complete();
        return count == 1;
    }

    private async Task<MemberMfaChallengeDto?> ValidateAsync(HttpContext context, AuthenticateResult result)
    {
        if (!result.Succeeded || !Guid.TryParse(result.Principal?.FindFirstValue(ChallengeClaimType), out var key)
            || !int.TryParse(result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var memberId)
            || !context.Request.Cookies.TryGetValue(BindingCookieName, out var binding) || binding.Length != 64) return null;
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var challenge = await scope.Database.FirstOrDefaultAsync<MemberMfaChallengeDto>(Sql.Builder.Where("challengeKey=@0 AND memberId=@1", key, memberId));
        if (challenge is null || challenge.ExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime
            || challenge.FailedAttempts >= MaxFailedAttempts
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(challenge.BindingHash), Encoding.ASCII.GetBytes(Hash(binding)))) return null;
        var member = await memberManager.FindByIdAsync(memberId.ToString());
        var credential = await mfaRepository.GetForMemberAsync(memberId);
        if (member is null || !member.IsApproved || await memberManager.IsLockedOutAsync(member)
            || member.SecurityStamp != challenge.SecurityStamp
            || (await staffRepository.GetByMemberIdAsync(memberId))?.IsActive != true
            || credential?.CredentialVersion != challenge.CredentialVersion)
        {
            await scope.Database.ExecuteAsync($"DELETE FROM {MemberMfaChallengeDto.TableName} WHERE challengeKey=@0", key);
            return null;
        }
        return challenge;
    }

    public async Task<bool> RecordFailedAttemptAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(SchemeName);
        if (!result.Succeeded || !Guid.TryParse(result.Principal?.FindFirstValue(ChallengeClaimType), out var key)
            || !int.TryParse(result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var memberId)) return false;
        using var scope = scopeProvider.CreateScope();
        var updated = await scope.Database.ExecuteAsync(
            $"UPDATE {MemberMfaChallengeDto.TableName} SET failedAttempts = failedAttempts + 1 WHERE challengeKey=@0 AND memberId=@1 AND expiresAtUtc>@2 AND failedAttempts<@3",
            key, memberId, timeProvider.GetUtcNow().UtcDateTime, MaxFailedAttempts);
        if (updated == 1)
        {
            var challenge = await scope.Database.FirstOrDefaultAsync<MemberMfaChallengeDto>(Sql.Builder.Where("challengeKey=@0", key));
            if (challenge?.FailedAttempts >= MaxFailedAttempts)
            {
                await scope.Database.ExecuteAsync($"DELETE FROM {MemberMfaChallengeDto.TableName} WHERE challengeKey=@0", key);
                scope.Complete();
                return false;
            }
        }
        scope.Complete();
        return updated == 1;
    }

    public async Task ClearAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(SchemeName);
        if (result.Succeeded && Guid.TryParse(result.Principal?.FindFirstValue(ChallengeClaimType), out var key))
        {
            using var scope = scopeProvider.CreateScope();
            await scope.Database.ExecuteAsync($"DELETE FROM {MemberMfaChallengeDto.TableName} WHERE challengeKey=@0", key);
            scope.Complete();
        }
        context.Response.Cookies.Delete(BindingCookieName, CookieOptions());
        await context.SignOutAsync(SchemeName);
    }

    private CookieOptions CookieOptions()
    {
        var securePolicy = Enum.TryParse<CookieSecurePolicy>(configuration["Security:MfaCookieSecurePolicy"], true, out var configuredPolicy)
            ? configuredPolicy
            : CookieSecurePolicy.SameAsRequest;

        return new CookieOptions
        {
            HttpOnly = true,
            Secure = securePolicy == CookieSecurePolicy.Always,
            SameSite = SameSiteMode.Strict,
            Path = "/staffops/account",
            MaxAge = TimeSpan.FromMinutes(5),
            IsEssential = true
        };
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
