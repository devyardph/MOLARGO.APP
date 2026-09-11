namespace DYS.Molargo.Shared.Data;

/// <summary>
/// Where the local SQLite file lives. Interface only, because only a head can answer it:
/// iOS wants the app's Library directory (excluded from iCloud backup), Android its
/// private data directory, Windows LocalApplicationData.
/// </summary>
public interface IDatabasePathProvider
{
    /// <summary>
    /// Absolute path to the database file. The implementation must create any missing
    /// directories — SQLite will not, and reports the failure as a bare "unable to open
    /// database file" that says nothing about the cause.
    /// </summary>
    string GetDatabasePath();
}
