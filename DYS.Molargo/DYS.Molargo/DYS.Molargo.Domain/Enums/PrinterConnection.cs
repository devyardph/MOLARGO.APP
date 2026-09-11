namespace DYS.Molargo.Domain.Enums;

/// <summary>How the receipt printer is attached to the terminal.</summary>
/// <remarks>
/// Recorded rather than detected. Nothing in the app opens a printer channel yet, and the
/// three differ in what identifies the device — a host name, a USB device name, or a
/// Bluetooth pairing name — which is why the field beside this one is labelled from here.
/// </remarks>
public enum PrinterConnection
{
    /// <summary>On the practice network, addressed by host name or IP.</summary>
    Network = 0,

    /// <summary>Plugged into this terminal.</summary>
    Usb = 1,

    /// <summary>Paired over Bluetooth, which only a device head could reach.</summary>
    Bluetooth = 2,
}
