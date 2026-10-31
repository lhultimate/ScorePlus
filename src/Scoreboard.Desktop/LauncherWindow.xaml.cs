using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public enum LaunchMode { New, Open }

/// <summary>
/// The first thing that opens: pick a new game, an existing one, or an autosave. It holds no game
/// state itself; the main window is created afterwards with the choice below.
/// </summary>
public partial class LauncherWindow : Window
{
    /// <summary>The release label shown on the side strip: a1.1, a1.02, and so on.</summary>
    public static string Version => AppInfo.Release;

    public LaunchMode Mode { get; private set; } = LaunchMode.New;

    public string? GamePath { get; private set; }

    // Deleting needs a second click, so a stray one never wipes the recovery points.
    readonly System.Windows.Threading.DispatcherTimer _deleteReset = new() { Interval = TimeSpan.FromSeconds(5) };
    bool _deleteArmed;

    public LauncherWindow()
    {
        InitializeComponent();
        VersionText.Text = Version;
        ThemeManager.SetSkipTheme(this, true);
        _deleteReset.Tick += (_, _) => ResetDeleteButton();
        RefreshAutosaves();
        Loaded += (_, _) => NewGameButton.Focus();
    }

    /// <summary>The recoverable points offered in the list, newest first.</summary>
    public IReadOnlyList<AutosaveEntry> Autosaves { get; private set; } = [];

    void RefreshAutosaves()
    {
        AutosaveStore.ImportLegacy();
        // Trim before showing the list, so a folder that grew across old builds is cleaned up.
        AutosaveStore.Trim();
        Autosaves = AutosaveStore.List();
        AutosaveList.ItemsSource = Autosaves;
        AutosaveEmpty.Visibility = Autosaves.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AutosaveList.Visibility = Autosaves.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        LoadAutosaveButton.IsEnabled = false;
        ClearAutosavesButton.IsEnabled = Autosaves.Count > 0;
        ResetDeleteButton();
        if (Autosaves.Count > 0) AutosaveList.SelectedIndex = 0;
    }

    /// <summary>Deletes every autosave, after a second click confirms it.</summary>
    void ClearAutosaves_Click(object sender, RoutedEventArgs e)
    {
        if (!_deleteArmed)
        {
            _deleteArmed = true;
            ClearAutosavesButton.Content = $"Delete {Autosaves.Count}?";
            AutosaveStatus.Text = "Click again to remove every autosave.";
            AutosaveStatus.Visibility = Visibility.Visible;
            _deleteReset.Stop();
            _deleteReset.Start();
            return;
        }
        var deleted = AutosaveStore.DeleteAll();
        RefreshAutosaves();
        AutosaveStatus.Text = deleted == 0
            ? "No autosaves were left to delete."
            : $"Deleted {deleted} autosave{(deleted == 1 ? "" : "s")}.";
        AutosaveStatus.Visibility = Visibility.Visible;
    }

    void ResetDeleteButton()
    {
        _deleteReset.Stop();
        _deleteArmed = false;
        if (ClearAutosavesButton is null) return;
        ClearAutosavesButton.Content = "Delete all";
    }

    void NewGame_Click(object sender, RoutedEventArgs e)
    {
        Mode = LaunchMode.New;
        GamePath = null;
        DialogResult = true;
        Close();
    }

    void OpenGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open game",
            DefaultExt = "spg",
            Filter = "ScorePlus Game (*.spg)|*.spg|Scoreboard Game (*.scoreboard;*.json)|*.scoreboard;*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        Finish(LaunchMode.Open, dialog.FileName);
    }

    void AutosaveList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => LoadAutosaveButton.IsEnabled = AutosaveList.SelectedItem is AutosaveEntry;

    void AutosaveList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => LoadSelectedAutosave();

    void LoadAutosave_Click(object sender, RoutedEventArgs e) => LoadSelectedAutosave();

    void LoadSelectedAutosave()
    {
        if (AutosaveList.SelectedItem is not AutosaveEntry entry) return;
        if (!File.Exists(entry.Path))
        {
            RefreshAutosaves();
            return;
        }
        Finish(LaunchMode.Open, entry.Path);
    }

    void Finish(LaunchMode mode, string path)
    {
        Mode = mode;
        GamePath = path;
        DialogResult = true;
        Close();
    }
}
