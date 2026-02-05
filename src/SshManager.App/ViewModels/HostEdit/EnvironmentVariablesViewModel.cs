using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;

namespace SshManager.App.ViewModels.HostEdit;

/// <summary>
/// ViewModel for managing environment variables in the host edit dialog.
/// Contains the collection of environment variables and commands for add/remove operations.
/// </summary>
public partial class EnvironmentVariablesViewModel : ObservableObject
{
    private readonly IHostEnvironmentVariableRepository? _envVarRepo;
    private readonly ILogger<EnvironmentVariablesViewModel> _logger;

    // Store original host ID for loading environment variables
    private Guid? _hostId;

    #region Properties

    /// <summary>
    /// Collection of environment variables for the host.
    /// Exposed as Items for XAML binding consistency.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoEnvironmentVariables))]
    private ObservableCollection<HostEnvironmentVariableViewModel> _items = [];

    /// <summary>
    /// Returns true if there are no environment variables configured.
    /// </summary>
    public bool HasNoEnvironmentVariables => Items.Count == 0;

    #endregion

    /// <summary>
    /// Creates a new instance of the EnvironmentVariablesViewModel.
    /// </summary>
    /// <param name="envVarRepo">Optional environment variable repository for data operations.</param>
    /// <param name="host">Optional host entry to load environment variables from.</param>
    /// <param name="logger">Optional logger.</param>
    public EnvironmentVariablesViewModel(
        IHostEnvironmentVariableRepository? envVarRepo = null,
        HostEntry? host = null,
        ILogger<EnvironmentVariablesViewModel>? logger = null)
    {
        _envVarRepo = envVarRepo;
        _logger = logger ?? NullLogger<EnvironmentVariablesViewModel>.Instance;

        // Store host ID for async loading
        if (host != null)
        {
            _hostId = host.Id;
        }
    }

    #region Async Load Methods

    /// <summary>
    /// Loads all async data (environment variables).
    /// Call this after constructing the ViewModel.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        await LoadEnvironmentVariablesAsync(ct);
    }

    /// <summary>
    /// Loads environment variables for the host asynchronously.
    /// Call this after constructing the ViewModel.
    /// </summary>
    public async Task LoadEnvironmentVariablesAsync(CancellationToken ct = default)
    {
        if (_envVarRepo == null || !_hostId.HasValue) return;

        try
        {
            var envVars = await _envVarRepo.GetByHostIdAsync(_hostId.Value, ct);
            Items = new ObservableCollection<HostEnvironmentVariableViewModel>(
                envVars.Select(e => new HostEnvironmentVariableViewModel
                {
                    Name = e.Name,
                    Value = e.Value,
                    IsEnabled = e.IsEnabled
                }));
            OnPropertyChanged(nameof(HasNoEnvironmentVariables));

            _logger.LogDebug("Loaded {EnvVarCount} environment variables for host", envVars.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load environment variables");
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Adds a new empty environment variable to the collection.
    /// </summary>
    [RelayCommand]
    private void AddEnvironmentVariable()
    {
        var envVar = new HostEnvironmentVariableViewModel
        {
            Name = string.Empty,
            Value = string.Empty,
            IsEnabled = true
        };
        Items.Add(envVar);
        OnPropertyChanged(nameof(HasNoEnvironmentVariables));
        _logger.LogDebug("Added new environment variable");
    }

    /// <summary>
    /// Removes an environment variable from the collection.
    /// </summary>
    [RelayCommand]
    private void RemoveEnvironmentVariable(HostEnvironmentVariableViewModel envVar)
    {
        if (envVar == null) return;

        Items.Remove(envVar);
        OnPropertyChanged(nameof(HasNoEnvironmentVariables));
        _logger.LogDebug("Removed environment variable: {EnvVarName}", envVar.Name);
    }

    /// <summary>
    /// Adds a preset environment variable from a "NAME=value" format string.
    /// Common presets include TERM, LANG, LC_ALL, EDITOR, etc.
    /// </summary>
    [RelayCommand]
    private void AddPresetEnvironmentVariable(string preset)
    {
        if (string.IsNullOrEmpty(preset)) return;

        var parts = preset.Split('=', 2);
        if (parts.Length != 2) return;

        var name = parts[0].Trim();
        var value = parts[1].Trim();

        // Check if this variable already exists
        if (Items.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogDebug("Environment variable {EnvVarName} already exists, skipping", name);
            return;
        }

        var envVar = new HostEnvironmentVariableViewModel
        {
            Name = name,
            Value = value,
            IsEnabled = true
        };
        Items.Add(envVar);
        OnPropertyChanged(nameof(HasNoEnvironmentVariables));
        _logger.LogDebug("Added preset environment variable: {EnvVarName}={EnvVarValue}", name, value);
    }

    #endregion

    #region Data Access Methods

    /// <summary>
    /// Gets the environment variables as domain models for saving.
    /// The caller should use IHostEnvironmentVariableRepository.SetForHostAsync() to save these.
    /// </summary>
    /// <param name="hostId">The host entry ID to associate with the environment variables.</param>
    /// <returns>Collection of HostEnvironmentVariable domain models.</returns>
    public IEnumerable<HostEnvironmentVariable> GetEnvironmentVariables(Guid hostId)
    {
        int sortOrder = 0;
        return Items
            .Where(e => !string.IsNullOrWhiteSpace(e.Name)) // Skip entries with empty names
            .Select(e => new HostEnvironmentVariable
            {
                Id = Guid.NewGuid(),
                HostEntryId = hostId,
                Name = e.Name.Trim(),
                Value = e.Value?.Trim() ?? string.Empty,
                IsEnabled = e.IsEnabled,
                SortOrder = sortOrder++
            });
    }

    /// <summary>
    /// Gets the count of valid environment variables (those with non-empty names).
    /// </summary>
    /// <returns>Count of valid environment variables.</returns>
    public int GetValidCount()
    {
        return Items.Count(e => !string.IsNullOrWhiteSpace(e.Name));
    }

    /// <summary>
    /// Clears all environment variables from the collection.
    /// </summary>
    public void Clear()
    {
        Items.Clear();
        OnPropertyChanged(nameof(HasNoEnvironmentVariables));
        _logger.LogDebug("Cleared all environment variables");
    }

    /// <summary>
    /// Sets the host ID for loading environment variables.
    /// Call LoadAsync() after this to load the variables.
    /// </summary>
    /// <param name="hostId">The host entry ID.</param>
    public void SetHostId(Guid hostId)
    {
        _hostId = hostId;
    }

    #endregion

    #region Preset Helpers

    /// <summary>
    /// Common environment variable presets that can be used with AddPresetEnvironmentVariable.
    /// </summary>
    public static IReadOnlyList<string> CommonPresets { get; } =
    [
        "TERM=xterm-256color",
        "LANG=en_US.UTF-8",
        "LC_ALL=en_US.UTF-8",
        "EDITOR=vim",
        "VISUAL=vim",
        "PAGER=less",
        "TZ=UTC"
    ];

    #endregion
}
