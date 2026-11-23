using System.Reflection;

namespace LetterboxImageCropper;

/// <summary>
/// Application name and version, read from the assembly attributes that the build generates from the project file
/// (Product, VersionPrefix/VersionSuffix). Never hardcode the version anywhere else.
/// </summary>
public static class AppInfo
{
    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    public static string Name { get; } =
        Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Letterbox Image Cropper";

    /// <summary>Semantic version, e.g. "1.2.0" or "1.3.0-beta.1".</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>Window title, e.g. "Letterbox Image Cropper v1.2.0".</summary>
    public static string Title => $"{Name} v{Version}";

    private static string ReadVersion()
    {
        string? informational = Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0]; // strip build metadata, if any

        return Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
