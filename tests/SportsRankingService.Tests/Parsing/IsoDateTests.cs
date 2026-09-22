using SportsRankingService.Parsing;

namespace SportsRankingService.Tests.Parsing;

/// <summary>Feeds write their ranking date as an ISO date or timestamp; only the calendar date is kept.</summary>
public class IsoDateTests
{
    [Theory]
    [InlineData("2026-09-12", 2026, 9, 12)]
    [InlineData("2026-09-14T00:00:00Z", 2026, 9, 14)]
    [InlineData("2026-09-10T07:00Z", 2026, 9, 10)]
    [InlineData("2026-09-01T00:00:00.000Z", 2026, 9, 1)]
    public void Keeps_the_calendar_date_of_an_iso_value(string text, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), IsoDate.Parse("Test", text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12/09/2026")]
    [InlineData("9/15/2026 6:53:21 PM")]
    public void Anything_else_fails_the_parse_naming_the_value(string? text)
    {
        var ex = Assert.Throws<ParseException>(() => IsoDate.Parse("Test", text));

        Assert.Contains("Test", ex.Message);
        Assert.Contains(text ?? "null", ex.Message);
    }
}
