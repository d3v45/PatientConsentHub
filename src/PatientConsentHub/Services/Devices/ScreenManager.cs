using PatientConsentHub.Models;
using PatientConsentHub.Services.Logging;
using WinForms = System.Windows.Forms;

namespace PatientConsentHub.Services.Devices;

/// <summary>
/// Lists connected displays. With a single monitor the app selects it silently
/// and hides the option entirely, per the simplicity requirement.
/// </summary>
public static class ScreenManager
{
    public static IReadOnlyList<DisplayDevice> GetDisplays()
    {
        var result = new List<DisplayDevice>();
        try
        {
            var screens = WinForms.Screen.AllScreens;
            for (var i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                result.Add(new DisplayDevice
                {
                    Index = i,
                    Name = $"Display {i + 1}",
                    X = s.Bounds.X,
                    Y = s.Bounds.Y,
                    Width = s.Bounds.Width,
                    Height = s.Bounds.Height,
                    IsPrimary = s.Primary
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Display enumeration failed.", ex);
        }

        if (result.Count == 0)
        {
            result.Add(new DisplayDevice
            {
                Index = 0, Name = "Display 1", X = 0, Y = 0,
                Width = 1920, Height = 1080, IsPrimary = true
            });
        }

        return result;
    }

    public static DisplayDevice Resolve(IReadOnlyList<DisplayDevice> displays, int preferredIndex)
    {
        var match = displays.FirstOrDefault(d => d.Index == preferredIndex);
        return match ?? displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
    }
}
