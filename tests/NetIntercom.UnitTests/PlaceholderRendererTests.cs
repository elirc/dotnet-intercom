using FluentAssertions;
using NetIntercom.Application.Common;
using NetIntercom.Application.Reports;
using Xunit;

namespace NetIntercom.UnitTests;

public class PlaceholderRendererTests
{
    [Fact]
    public void ReplacesKnownPlaceholders()
    {
        var result = PlaceholderRenderer.Render(
            "Hi {{visitor.name}}, I'm {{agent.name}}!",
            new Dictionary<string, string?> { ["visitor.name"] = "Pat", ["agent.name"] = "Sara" });

        result.Should().Be("Hi Pat, I'm Sara!");
    }

    [Fact]
    public void LeavesUnknownOrEmptyPlaceholdersIntact()
    {
        var result = PlaceholderRenderer.Render(
            "Hi {{visitor.name}}, ref {{ticket.id}}",
            new Dictionary<string, string?> { ["visitor.name"] = null });

        result.Should().Be("Hi {{visitor.name}}, ref {{ticket.id}}");
    }

    [Fact]
    public void ToleratesWhitespaceInsideBraces()
    {
        PlaceholderRenderer.Render("{{ visitor.name }}", new Dictionary<string, string?> { ["visitor.name"] = "Pat" })
            .Should().Be("Pat");
    }
}

public class MedianTests
{
    [Theory]
    [InlineData(new double[] { 1, 2, 3 }, 2)]
    [InlineData(new double[] { 1, 2, 3, 4 }, 2.5)]
    [InlineData(new double[] { 5 }, 5)]
    public void Median_IsComputedCorrectly(double[] values, double expected)
    {
        GetOverviewReportHandler.Median(values.ToList()).Should().Be(expected);
    }

    [Fact]
    public void Median_OfEmpty_IsNull()
    {
        GetOverviewReportHandler.Median(new List<double>()).Should().BeNull();
    }
}
