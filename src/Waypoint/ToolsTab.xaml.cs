using System.Windows;
using System.Windows.Controls;

namespace Waypoint;

/// <summary>Tools for the game itself, each in its own window: the item crafter, the map viewer, and the tools the checkout
/// adds (a folder under Tools\ or Mods\Binaries\ with a waypoint-tool.json beside its script, read when the section shows).</summary>
public partial class ToolsTab : UserControl
{
    Shared _shared = null!;
    ItemCrafterWindow? _crafter;
    MapViewerWindow? _mapViewer;
    CheckoutToolCards _checkoutTools = null!;

    /// <summary>The player wants a copy of LocalExile that has the crafter.</summary>
    internal event Action? NewCopyRequested;

    public ToolsTab()
    {
        InitializeComponent();
        _checkoutTools = new CheckoutToolCards(Cards);
        // A checkout switch, or a tool added while Waypoint runs, is picked up when the section is shown again.
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) RefreshTools(); };
    }

    internal void Attach(Shared shared)
    {
        _shared = shared;
        MapButton.IsEnabled = !Repo.IsInstall && MapViewerHelper.IsAvailable(Environment.CurrentDirectory);
        MapButton.ToolTip = MapButton.IsEnabled ? null : L.Text("The map viewer needs a source checkout with the terrain generator and extracted game data.");
        ToolTipService.SetShowOnDisabled(MapButton, true);
        // Asking a Gateway for a mode it lacks starts it serving, so a release's is not asked.
        if (Repo.IsInstall)
        {
            CrafterButton.IsEnabled = false;
            ReleaseNote.Visibility = Visibility.Visible;
            // A release install brings no checkout: there are no tools to find, nothing to refresh.
            RefreshButton.Visibility = Visibility.Collapsed;
            CheckoutNote.Visibility = Visibility.Collapsed;
            return;
        }
        RefreshTools();
    }

    void RefreshClicked(object sender, RoutedEventArgs e) => RefreshTools();

    /// <summary>The checkout's tools, read again; the item crafter's card never changes.</summary>
    void RefreshTools()
    {
        if (Repo.IsInstall) return;
        _checkoutTools.Refresh();
        NoLocalTools.Visibility = _checkoutTools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void GetCrafterClicked(object sender, RoutedEventArgs e) => NewCopyRequested?.Invoke();

    void OpenMapViewer(object sender, RoutedEventArgs e)
    {
        if (_mapViewer is { IsLoaded: true }) { _mapViewer.Activate(); return; }
        _mapViewer = new MapViewerWindow(_shared);
        _mapViewer.Closed += (_, _) => _mapViewer = null;
        _mapViewer.Show();
    }

    /// <summary>The crafter's window, or the one already open brought forward.</summary>
    void OpenItemCrafter(object sender, RoutedEventArgs e)
    {
        if (_crafter is { IsLoaded: true })
        {
            if (_crafter.WindowState == WindowState.Minimized) _crafter.WindowState = WindowState.Normal;
            _crafter.Activate();
            return;
        }
        // A window of its own, not owned by Waypoint's: either can be in front. It opens centred on Waypoint's.
        _crafter = new ItemCrafterWindow(_shared);
        if (Window.GetWindow(this) is { WindowState: WindowState.Normal, IsVisible: true } main)
        {
            _crafter.WindowStartupLocation = WindowStartupLocation.Manual;
            _crafter.Left = main.Left + (main.ActualWidth - _crafter.Width) / 2;
            _crafter.Top = main.Top + (main.ActualHeight - _crafter.Height) / 2;
        }
        else _crafter.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _crafter.Closed += (_, _) => _crafter = null;
        _crafter.Show();
    }
}
