using System.Text.Json;

namespace Launcher.Core;

/// <summary>A tool a LocalExile checkout offers Waypoint: a folder with a <see cref="CheckoutTools.DescriptorName"/>
/// beside its script. Waypoint only launches it; the tool lives in the checkout and may change without touching Waypoint.</summary>
public sealed record CheckoutTool(
    string Name,
    string Description,
    string Script,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string Folder,
    bool NeedsGame,
    string? Icon);

/// <summary>Reads the <c>waypoint-tool.json</c> files a checkout keeps beside its own tools, so a tool can be added to the
/// checkout without a Waypoint change. Two folders are looked at, one level down: <c>Tools\</c> and <c>Mods\Binaries\</c>.</summary>
public static class CheckoutTools
{
    public const string DescriptorName = "waypoint-tool.json";

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Every tool this checkout offers, by name. A descriptor that is broken, or names a file outside the checkout,
    /// is skipped: one bad tool never hides the others.</summary>
    public static IReadOnlyList<CheckoutTool> Find(string checkoutRoot)
    {
        if (string.IsNullOrWhiteSpace(checkoutRoot)) return [];
        string root;
        try { root = Path.GetFullPath(checkoutRoot); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return []; }
        var tools = new List<CheckoutTool>();
        foreach (string folder in ToolFolders(root))
        {
            string descriptor = Path.Combine(folder, DescriptorName);
            if (Read(descriptor, root) is { } tool) tools.Add(tool);
        }
        return [.. tools.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>One descriptor, from a checkout's tool folder. Null when it cannot be read or does not point at a script
    /// inside the checkout.</summary>
    public static CheckoutTool? Read(string descriptor, string checkoutRoot)
    {
        string root;
        try { root = Path.GetFullPath(checkoutRoot); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        Descriptor? parsed;
        try { parsed = JsonSerializer.Deserialize<Descriptor>(File.ReadAllText(descriptor), Json); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
        if (parsed is null) return null;
        string name = (parsed.Name ?? "").Trim();
        string script = (parsed.Script ?? "").Trim();
        if (name.Length == 0 || script.Length == 0) return null;
        if (name.Length > 120) name = name[..120];
        string description = (parsed.Description ?? "").Trim();
        if (description.Length > 2000) description = description[..2000];
        string folder = Path.GetDirectoryName(Path.GetFullPath(descriptor)) ?? root;
        string? resolved = Resolve(root, folder, script);
        if (resolved is null || !File.Exists(resolved)) return null;
        // The folder the tool name and description came from is the default place it runs from, as its .cmd would.
        string? working = parsed.WorkingDirectory is { Length: > 0 } wanted ? Resolve(root, folder, wanted) : folder;
        if (working is null || !Directory.Exists(working)) return null;
        string[] arguments = parsed.Arguments is { Length: > 0 } given
            ? [.. given.Where(a => a is { Length: > 0 } && a.Length <= 1024).Take(32)]
            : [];
        return new(name, description, resolved, arguments, working, folder, parsed.NeedsGame, parsed.Icon);
    }

    /// <summary>A relative path inside the checkout, or null when it is rooted or would leave the checkout.</summary>
    static string? Resolve(string root, string folder, string relative)
    {
        try
        {
            string path = Path.GetFullPath(Path.Combine(folder, relative));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    static IEnumerable<string> ToolFolders(string root)
    {
        foreach (string parent in new[] { Path.Combine(root, "Tools"), Path.Combine(root, "Mods", "Binaries") })
        {
            string[] folders;
            try { folders = Directory.GetDirectories(parent); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { continue; }
            foreach (string folder in folders) yield return folder;
        }
    }

    sealed class Descriptor
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Script { get; set; }
        public string[]? Arguments { get; set; }
        public string? WorkingDirectory { get; set; }
        public bool NeedsGame { get; set; }
        public string? Icon { get; set; }
    }
}
