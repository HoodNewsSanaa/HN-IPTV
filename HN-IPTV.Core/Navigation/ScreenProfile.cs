namespace HnIptv.Navigation;

public enum ScreenProfile { Desktop, Television, Phone, Tablet }

/// <summary>
/// Presentation profile is separate from the OS: an Android phone and an Android TV
/// need different navigation; a large Windows touchscreen may use tablet layout.
/// </summary>
public interface IScreenProfileProvider
{
    ScreenProfile CurrentProfile { get; }
}
