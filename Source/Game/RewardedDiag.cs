namespace GardenGuardians;

/// <summary>
/// A breadcrumb trail for the rewarded video, kept in a small text file beside the saved garden: every step is written (and closed) before the next
/// starts, so if the app dies the last line says where. The Settings page's Ad test shows it (see Game.AdTest). Written from both C# and the Java bridge.
/// </summary>
internal static class RewardedDiag
{
    private static readonly object Gate = new();

    public static string Path => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SaveSystem.DefaultPath)!, "rewarded-log.txt");

    public static void Step(string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss} {text}\n");
            }
        }
        catch
        {
            // (a log that cannot be written is not worth a crash)
        }
    }

    /// <summary>The last <paramref name="count"/> lines.</summary>
    public static string[] Tail(int count)
    {
        try
        {
            lock (Gate)
                return File.Exists(Path) ? File.ReadAllLines(Path).TakeLast(count).ToArray() : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static void Clear()
    {
        try
        {
            lock (Gate)
                File.Delete(Path);
        }
        catch
        {
            // (ignored)
        }
    }
}
