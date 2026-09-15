using System.Reflection;

namespace Toaster.Core;

/// <summary>
/// The release version, taken from the assembly rather than a literal. Every project
/// inherits &lt;Version&gt; from Directory.Build.props, so the status endpoint, the MCP
/// handshake and the installer cannot drift apart.
/// </summary>
public static class ToasterVersion
{
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var informational = typeof(ToasterVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // The SDK appends "+<commit sha>" when the build knows its source revision.
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }

        return typeof(ToasterVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
