namespace Teleporter;

internal static class BuildInfo
{
    public const string Name = "Teleporter";
    public const string Description = "Choose a destination scene and travel there through the game's own transition path.";
    public const string Author = "Lycanthor";
    public const string Version = "0.1.0";

    public static string Stamp => $"{Name} {Version} built {System.IO.File.GetLastWriteTimeUtc(typeof(BuildInfo).Assembly.Location):yyyy-MM-dd HH:mm:ss}Z";
}
