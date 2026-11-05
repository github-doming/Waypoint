using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.Core;
using MaterialDesignThemes.Wpf;

namespace Waypoint;

/// <summary>The tools this checkout offers Waypoint, as cards in the Tools section: each is a folder under Tools\ or
/// Mods\Binaries\ with a waypoint-tool.json beside its script. Nothing here names a tool, so a checkout can add one
/// without a Waypoint change; a tool is a checkout file, which is what keeps this compatible with the server repository
/// changing under it.</summary>
sealed class CheckoutToolCards
{
    readonly WrapPanel _cards;
    readonly List<FrameworkElement> _shown = [];
    readonly Dictionary<string, Process> _running = new(StringComparer.OrdinalIgnoreCase);

    internal CheckoutToolCards(WrapPanel cards) => _cards = cards;

    /// <summary>How many cards the last <see cref="Refresh"/> found.</summary>
    internal int Count { get; private set; }

    /// <summary>Reads the descriptors again. A tool that is already open is left running, and the item crafter's own card
    /// in the same row stays where it is: only the cards this class added are replaced.</summary>
    internal void Refresh()
    {
        foreach (var shown in _shown) _cards.Children.Remove(shown);
        _shown.Clear();
        var tools = CheckoutTools.Find(Environment.CurrentDirectory);
        Count = tools.Count;
        foreach (var tool in tools)
        {
            var card = Card(tool);
            _shown.Add(card);
            _cards.Children.Add(card);
        }
    }

    FrameworkElement Card(CheckoutTool tool)
    {
        var status = new TextBlock
        {
            Style = Resource<Style>("Shell.Secondary"),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var open = new Button
        {
            Content = L.Text("Open"),
            Style = Resource<Style>("Shell.PrimaryButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var folder = new Button
        {
            Content = L.Text("Open folder"),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        open.Click += (_, _) => Open(tool, status);
        folder.Click += (_, _) => OpenFolder(tool.Folder);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(open);
        buttons.Children.Add(folder);

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = tool.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (tool.Description.Length > 0)
            text.Children.Add(new TextBlock
            {
                Text = tool.Description,
                Style = Resource<Style>("Shell.Secondary"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        if (tool.NeedsGame)
            text.Children.Add(new TextBlock
            {
                Text = L.Text("This tool needs the game running."),
                Style = Resource<Style>("Shell.Secondary"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        text.Children.Add(status);

        var icon = new PackIcon
        {
            Kind = IconOf(tool.Icon),
            Width = 36,
            Height = 36,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = Resource<Brush>("Shell.Accent"),
        };
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Bottom);

        var dock = new DockPanel();
        dock.Children.Add(buttons);
        dock.Children.Add(icon);
        dock.Children.Add(text);

        // The item crafter's card in ToolsTab.xaml is the model: same width, padding, corner and border.
        return new Border
        {
            Width = 340,
            Margin = new Thickness(0, 0, 14, 14),
            Padding = new Thickness(18, 16, 18, 16),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = Resource<Brush>("Shell.Panel"),
            BorderBrush = Resource<Brush>("Shell.Border"),
            Child = dock,
        };
    }

    void Open(CheckoutTool tool, TextBlock status)
    {
        if (_running.TryGetValue(tool.Script, out var running) && !running.HasExited)
        {
            Say(status, L.Text("Already open"));
            return;
        }
        try
        {
            var process = LocalToolRun.Start(tool);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => _cards.Dispatcher.BeginInvoke(() =>
            {
                if (_running.TryGetValue(tool.Script, out var current) && current == process) _running.Remove(tool.Script);
            });
            _running[tool.Script] = process;
            Say(status, L.Text("Opened"));
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Say(status, L.Text("Could not start"));
        }
    }

    void OpenFolder(string folder)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true }); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException) { }
    }

    static void Say(TextBlock label, string text)
    {
        label.Text = text;
        label.Visibility = Visibility.Visible;
    }

    static T? Resource<T>(string key) where T : class => Application.Current?.TryFindResource(key) as T;

    static PackIconKind IconOf(string? name) =>
        Enum.TryParse<PackIconKind>(name, ignoreCase: true, out var kind) ? kind : PackIconKind.Toolbox;
}

/// <summary>Starts a checkout tool in its own process, with the checkout's PowerShell and no console of its own: the tool's
/// own WinForms window is the only thing the player sees, as when its .cmd is double-clicked.</summary>
static class LocalToolRun
{
    internal static Process Start(CheckoutTool tool) =>
        Process.Start(StartInfo(tool)) ?? throw new InvalidOperationException("the tool process did not start");

    /// <summary>The command its .cmd would run: the checkout's PowerShell, STA (the tools are WinForms), no console window,
    /// the script, then the descriptor's arguments. The console is suppressed with CreateNoWindow rather than a hidden
    /// window style: STARTUPINFO's SW_HIDE hides the first window a process shows, which is the tool's own form.</summary>
    internal static ProcessStartInfo StartInfo(CheckoutTool tool)
    {
        string shell = Platform.OnPath("pwsh.exe") ?? Platform.PowerShell;
        var info = new ProcessStartInfo(shell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = tool.WorkingDirectory,
        };
        foreach (string argument in new[] { "-NoProfile", "-Sta", "-ExecutionPolicy", "Bypass", "-File", tool.Script })
            info.ArgumentList.Add(argument);
        foreach (string argument in tool.Arguments) info.ArgumentList.Add(argument);
        return info;
    }
}
