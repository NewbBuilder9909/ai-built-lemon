using ProgrammePulse.Services.Security;

namespace ProgrammePulse.Tests.Security;

public class MfaLockoutPolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(11)]
    [InlineData(19)]
    [InlineData(99)]
    public void Only_every_tenth_consecutive_failure_locks(int failures) =>
        Assert.Null(MfaVerificationService.LockUntilAfter(failures, Now));

    [Theory]
    [InlineData(10, 15)]
    [InlineData(20, 30)]
    [InlineData(30, 60)]
    [InlineData(70, 16 * 60)]
    [InlineData(80, 24 * 60)]
    [InlineData(90, 24 * 60)]
    public void Each_lock_doubles_the_last_up_to_a_day(int failures, int minutes) =>
        Assert.Equal(Now.AddMinutes(minutes), MfaVerificationService.LockUntilAfter(failures, Now));

    [Theory]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public void The_hundredth_consecutive_failure_locks_until_an_admin_resets(int failures) =>
        Assert.Equal(MfaVerificationService.LockedUntilReset, MfaVerificationService.LockUntilAfter(failures, Now));

    [Fact]
    public void A_patient_attacker_gets_one_hundred_guesses_in_all()
    {
        // The whole point of escalating: the lifetime budget is fixed, not
        // a rate. Walk the schedule, waiting out every lock.
        var now = Now;
        var guesses = 0;
        DateTime? until;
        do
        {
            guesses++;
            until = MfaVerificationService.LockUntilAfter(guesses, now);
            if (until is { } u && u < MfaVerificationService.LockedUntilReset) now = u;
        }
        while (until != MfaVerificationService.LockedUntilReset);

        Assert.Equal(MfaVerificationService.MaxConsecutiveFailures, guesses);
        Assert.True(now - Now < TimeSpan.FromDays(4), "The schedule should reach the hard stop within days, not months.");
    }
}
