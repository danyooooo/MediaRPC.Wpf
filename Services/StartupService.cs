using Microsoft.Win32;

namespace MediaRPC.Services;

/// <summary>
/// Manages Windows startup registration via registry.
/// </summary>
public class StartupService
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MediaRPC";

    /// <summary>
    /// Gets or sets whether the app runs at Windows startup.
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
            return key?.GetValue(AppName) != null;
        }
        set
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            if (key == null) return;

            if (value)
            {
                // Get the executable path and add --startup flag
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\" --startup");
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
    }
}
