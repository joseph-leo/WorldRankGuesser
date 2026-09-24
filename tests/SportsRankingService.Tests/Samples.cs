namespace SportsRankingService.Tests;

/// <summary>A synthetic response from Samples/, written by Samples/generate.mjs; see Samples/SAMPLES.md.</summary>
internal static class Sample
{
    public static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", fileName));
}

/// <summary>
/// A real federation response saved under Captures/ with the same name as its sample. The folder is
/// git-ignored and optional: CaptureTests run when it exists next to the test binaries and are skipped otherwise.
/// </summary>
internal static class Capture
{
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Captures");

    public static bool Available => System.IO.Directory.Exists(Directory);

    public static string Read(string fileName) => File.ReadAllText(Path.Combine(Directory, fileName));
}

/// <summary>A theory that runs only when the Captures folder exists.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CaptureTheoryAttribute : TheoryAttribute
{
    public CaptureTheoryAttribute()
    {
        if (!Capture.Available)
        {
            Skip = "No Captures folder next to the test binaries; see Samples/SAMPLES.md for how to capture real responses.";
        }
    }
}
