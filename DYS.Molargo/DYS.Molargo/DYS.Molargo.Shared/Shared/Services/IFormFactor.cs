namespace DYS.Molargo.Shared.Services;

/// <summary>
/// What kind of device is rendering. Interface only: each head answers it for itself,
/// which is the whole reason it exists — the shared UI adapts a surgery tablet layout
/// from a front-desk browser layout without either head owning a screen.
/// </summary>
public interface IFormFactor
{
    /// <summary>"Desktop", "Phone", "Tablet" — the shape of the screen.</summary>
    string GetFormFactor();

    /// <summary>The host OS and version, for diagnostics and the support footer.</summary>
    string GetPlatform();
}
