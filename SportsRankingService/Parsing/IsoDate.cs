using System.Globalization;

namespace SportsRankingService.Parsing;

/// <summary>Reads the calendar date at the start of an ISO 8601 date or timestamp ("2026-09-12", "2026-09-12T07:00Z").</summary>
public static class IsoDate
{
    public static DateOnly Parse(string sourceName, string? text)
    {
        if (text is { Length: >= 10 }
            && DateOnly.TryParseExact(text.AsSpan(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            return date;
        }

        throw new ParseException(sourceName, $"ranking date '{text ?? "null"}' is not an ISO date");
    }
}
