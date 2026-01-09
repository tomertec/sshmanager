using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.App.Services;
using SshManager.Core.Models;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the PuTTY session import dialog.
/// </summary>
public partial class PuttyImportViewModel : ObservableObject, IDisposable
{
    private readonly IPuttySessionImporter _importer;

    [ObservableProperty]
    private ObservableCollection<PuttySessionItem> _sessions = [];

    [ObservableProperty]
    private ObservableCollection<string> _warnings = [];

    [ObservableProperty]
    private ObservableCollection<string> _errors = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasSessions;

    [ObservableProperty]
    private bool _hasWarnings;

    [ObservableProperty]
    private bool _hasErrors;

    [ObservableProperty]
    private bool _isPuttyInstalled;

    [ObservableProperty]
    private int _selectedCount;

    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;

    public PuttyImportViewModel(IPuttySessionImporter importer)
    {
        _importer = importer;
    }

    /// <summary>
    /// Loads PuTTY sessions from the registry.
    /// </summary>
    [RelayCommand]
    private void LoadSessions()
    {
        IsLoading = true;
        Sessions.Clear();
        Warnings.Clear();
        Errors.Clear();

        try
        {
            var result = _importer.GetAllSessions();

            IsPuttyInstalled = result.IsPuttyInstalled;

            foreach (var session in result.Sessions)
            {
                var item = new PuttySessionItem(session) { IsSelected = true };
                item.PropertyChanged += OnSessionItemPropertyChanged;
                Sessions.Add(item);
            }

            foreach (var warning in result.Warnings)
            {
                Warnings.Add(warning);
            }

            foreach (var error in result.Errors)
            {
                Errors.Add(error);
            }

            HasSessions = Sessions.Count > 0;
            HasWarnings = Warnings.Count > 0;
            HasErrors = Errors.Count > 0;
            UpdateSelectedCount();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnSessionItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PuttySessionItem.IsSelected))
        {
            UpdateSelectedCount();
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var session in Sessions)
        {
            session.IsSelected = true;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var session in Sessions)
        {
            session.IsSelected = false;
        }
        UpdateSelectedCount();
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = Sessions.Count(s => s.IsSelected);
    }

    [RelayCommand]
    private void Import()
    {
        DialogResult = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Gets the selected sessions converted to HostEntry objects.
    /// </summary>
    public List<HostEntry> GetSelectedHosts()
    {
        return Sessions
            .Where(s => s.IsSelected)
            .Select(s => _importer.ConvertToHostEntry(s.Session))
            .ToList();
    }

    public void Dispose()
    {
        foreach (var session in Sessions)
        {
            session.PropertyChanged -= OnSessionItemPropertyChanged;
        }
        Sessions.Clear();
    }
}

/// <summary>
/// Wrapper for PuttySession with selection state and display properties.
/// </summary>
public partial class PuttySessionItem : ObservableObject
{
    public PuttySession Session { get; }

    [ObservableProperty]
    private bool _isSelected;

    // Display properties for DataGrid binding
    public string DisplayName => Session.Name;
    public string DisplayHostName => Session.HostName ?? "";
    public int DisplayPort => Session.Port;
    public string DisplayUserName => Session.UserName ?? Environment.UserName;
    public string DisplayAuthType => !string.IsNullOrEmpty(Session.PrivateKeyFile) ? "Private Key" : "SSH Agent";
    public bool HasPpkWarning => Session.PrivateKeyFile?.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase) == true;

    public PuttySessionItem(PuttySession session)
    {
        Session = session;
    }
}
