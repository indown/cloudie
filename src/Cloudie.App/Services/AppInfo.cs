using System.Reflection;
using Cloudie.Core.Updates;

namespace Cloudie.App.Services;

/// <summary>
/// The version baked into the build. CI passes <c>-p:Version</c> (the tag, or <c>0.1.0-ci.N</c>) and
/// <c>-p:SourceRevisionId</c> (the short commit), which the SDK joins into the informational version.
/// </summary>
public static class AppInfo
{
    public static string InformationalVersion { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public static SemanticVersion Version { get; } =
        SemanticVersion.TryParse(InformationalVersion, out var version) ? version : new SemanticVersion(0, 0, 0);

    /// <summary>"0.1.0-ci.12 (89ba1b8)": the version plus the commit when the build recorded one.</summary>
    public static string VersionText
    {
        get
        {
            var plus = InformationalVersion.IndexOf('+');
            if (plus < 0) return InformationalVersion;
            var commit = InformationalVersion[(plus + 1)..];
            return $"{InformationalVersion[..plus]} ({commit[..Math.Min(7, commit.Length)]})";
        }
    }
}
