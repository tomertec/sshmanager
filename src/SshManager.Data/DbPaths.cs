namespace SshManager.Data;

/// <summary>
/// Provides paths to database and application data directories.
/// </summary>
public static class DbPaths
{
    /// <summary>
    /// Gets the application data directory, creating it if necessary.
    /// </summary>
    public static string GetAppDataDir()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dir = Path.Combine(baseDir, "SshManager");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Gets the full path to the SQLite database file.
    /// </summary>
    public static string GetDbPath() => Path.Combine(GetAppDataDir(), "sshmanager.db");
}
