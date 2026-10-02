using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.Shared;

/// <summary>
/// A page request built from a query string is bounded both ways, so no URL can
/// overflow the offset or force an arbitrarily deep scan (Aikido: uncontrolled
/// resource consumption).
/// </summary>
public sealed class PageRequestTests
{
    [Theory]
    [InlineData(int.MaxValue, PageRequest.MaxNumber)]
    [InlineData(2_000_000_000, PageRequest.MaxNumber)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public void The_page_number_is_clamped(int requested, int expected)
    {
        Assert.Equal(expected, new PageRequest(requested).Number);
    }

    [Fact]
    public void The_deepest_page_at_the_largest_size_never_overflows_the_offset()
    {
        var deepest = new PageRequest(int.MaxValue, int.MaxValue);

        Assert.Equal(PageRequest.MaxSize, deepest.Size);
        Assert.True(deepest.Skip >= 0);
        Assert.Equal((PageRequest.MaxNumber - 1) * PageRequest.MaxSize, deepest.Skip);
    }
}
