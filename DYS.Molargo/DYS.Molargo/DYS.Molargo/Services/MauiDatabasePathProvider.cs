using DYS.Molargo.Shared.Data;

namespace DYS.Molargo.Services;

/// <summary>
/// Where the local SQLite file lives on a device.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FileSystem.AppDataDirectory"/> is the private, per-app, backed-up location
/// on every platform MAUI targets — Library/ on iOS, the app's files directory on Android,
/// LocalApplicationData on Windows. The cache directory would be wrong: the OS is free to
/// delete it under storage pressure, and while the app is offline-only this file is the
/// only copy of the practice's data.
/// </para>
/// <para>
/// The directory is created here because SQLite will not, and reports a missing one as a
/// bare "unable to open database file" that says nothing about the cause.
/// </para>
/// </remarks>
public sealed class MauiDatabasePathProvider : IDatabasePathProvider
{
    private const string FileName = "molargo.db3";

    public string GetDatabasePath()
    {
        var directory = FileSystem.AppDataDirectory;
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, FileName);
    }
}
