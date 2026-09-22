namespace SportsRankingService.Tests;

/// <summary>
/// Loads a saved real federation response from the Fixtures folder.
/// Each file is one feed's response captured on the date noted in FIXTURES.md.
/// </summary>
internal static class Fixture
{
    public static string Read(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        return File.ReadAllText(path);
    }
}
