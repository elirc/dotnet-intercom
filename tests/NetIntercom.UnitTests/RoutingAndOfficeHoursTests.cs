using FluentAssertions;
using NetIntercom.Domain.Entities;
using Xunit;

namespace NetIntercom.UnitTests;

public class RoutingRuleTests
{
    [Theory]
    [InlineData("I have a billing problem", null, true)]
    [InlineData("hello there", "https://example.com/billing/plans", true)]
    [InlineData("hello there", "https://example.com/docs", false)]
    public void KeywordRule_MatchesMessageOrSourceUrl(string message, string? url, bool expected)
    {
        var rule = new RoutingRule { Keyword = "billing", IsEnabled = true };
        rule.Matches(message, url).Should().Be(expected);
    }

    [Fact]
    public void NullKeyword_IsCatchAll()
    {
        new RoutingRule { IsEnabled = true }.Matches("anything", null).Should().BeTrue();
    }

    [Fact]
    public void DisabledRule_NeverMatches()
    {
        new RoutingRule { Keyword = null, IsEnabled = false }.Matches("anything", null).Should().BeFalse();
    }
}

public class OfficeHoursTests
{
    private static OfficeHours WeekdayNineToFive => new()
    {
        TimeZoneId = "UTC",
        Windows = new List<OfficeHoursWindow>
        {
            new() { Day = DayOfWeek.Tuesday, Start = new TimeOnly(9, 0), End = new TimeOnly(17, 0) }
        }
    };

    [Fact]
    public void InsideWindow_IsOpen()
    {
        // 2026-06-09 is a Tuesday.
        WeekdayNineToFive.IsOpenAt(new DateTime(2026, 6, 9, 12, 0, 0, DateTimeKind.Utc)).Should().BeTrue();
    }

    [Fact]
    public void OutsideWindow_IsClosed()
    {
        WeekdayNineToFive.IsOpenAt(new DateTime(2026, 6, 9, 20, 0, 0, DateTimeKind.Utc)).Should().BeFalse();
    }

    [Fact]
    public void OtherDay_IsClosed()
    {
        // Wednesday
        WeekdayNineToFive.IsOpenAt(new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc)).Should().BeFalse();
    }

    [Fact]
    public void NoWindows_AlwaysOpen()
    {
        new OfficeHours().IsOpenAt(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void JsonRoundTrip_Works()
    {
        var json = WeekdayNineToFive.ToJson();
        var restored = OfficeHours.FromJson(json)!;
        restored.Windows.Should().HaveCount(1);
        restored.Windows[0].Start.Should().Be(new TimeOnly(9, 0));
    }
}
