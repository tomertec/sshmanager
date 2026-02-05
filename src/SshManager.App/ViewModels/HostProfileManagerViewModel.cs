using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SshManager.Core.Models;
using SshManager.Data.Repositories;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for managing host profiles (CRUD operations).
/// </summary>
public partial class HostProfileManagerViewModel : ObservableObject
{
    private readonly IHostProfileRepository _profileRepository;
    private readonly IProxyJumpProfileRepository _proxyJumpRepository;
    private readonly ILogger<HostProfileManagerViewModel> _logger;
    private readonly ILoggerFactory _loggerFactory;

    [ObservableProperty]
    private ObservableCollection<HostProfile> _profiles = [];

    [ObservableProperty]
    private HostProfile? _selectedProfile;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public event Action? RequestClose;

    public HostProfileManagerViewModel(
        IHostProfileRepository profileRepository,
        IProxyJumpProfileRepository proxyJumpRepository,
        ILogger<HostProfileManagerViewModel>? logger,
        ILoggerFactory? loggerFactory = null)
    {
        _profileRepository = profileRepository;
        _proxyJumpRepository = proxyJumpRepository;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HostProfileManagerViewModel>.Instance;
        _loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
    }

    /// <summary>
    /// Loads all host profiles from the repository.
    /// </summary>
    public async Task LoadProfilesAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var profiles = await _profileRepository.GetAllAsync(ct);
            Profiles = new ObservableCollection<HostProfile>(profiles);
            _logger.LogDebug("Loaded {Count} host profiles", profiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load host profiles");
            ErrorMessage = $"Failed to load profiles: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Adds a new host profile.
    /// </summary>
    [RelayCommand]
    private async Task AddProfileAsync()
    {
        var dialog = new Views.Dialogs.HostProfileDialog
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        var viewModel = new HostProfileDialogViewModel(
            _proxyJumpRepository,
            _loggerFactory.CreateLogger<HostProfileDialogViewModel>());

        dialog.DataContext = viewModel;
        await viewModel.LoadProxyJumpProfilesAsync();

        viewModel.RequestClose += () => dialog.Close();

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var profile = viewModel.GetProfile();
                await _profileRepository.AddAsync(profile);
                Profiles.Add(profile);
                SelectedProfile = profile;
                _logger.LogInformation("Added new host profile: {DisplayName}", profile.DisplayName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add host profile");
                ErrorMessage = $"Failed to add profile: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Edits the selected host profile.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEditOrDelete))]
    private async Task EditProfileAsync()
    {
        if (SelectedProfile == null) return;

        var dialog = new Views.Dialogs.HostProfileDialog
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        var viewModel = new HostProfileDialogViewModel(
            _proxyJumpRepository,
            _loggerFactory.CreateLogger<HostProfileDialogViewModel>(),
            SelectedProfile);

        dialog.DataContext = viewModel;
        await viewModel.LoadProxyJumpProfilesAsync();

        viewModel.RequestClose += () => dialog.Close();

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var profile = viewModel.GetProfile();
                await _profileRepository.UpdateAsync(profile);

                // Refresh the list
                var index = Profiles.IndexOf(SelectedProfile);
                if (index >= 0)
                {
                    Profiles[index] = profile;
                    SelectedProfile = profile;
                }

                _logger.LogInformation("Updated host profile: {DisplayName}", profile.DisplayName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update host profile");
                ErrorMessage = $"Failed to update profile: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Deletes the selected host profile.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEditOrDelete))]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile == null) return;

        var result = System.Windows.MessageBox.Show(
            $"Are you sure you want to delete the profile '{SelectedProfile.DisplayName}'?\n\n" +
            "Hosts using this profile will not be deleted, but they will lose the profile reference.",
            "Delete Profile",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            try
            {
                await _profileRepository.DeleteAsync(SelectedProfile.Id);
                Profiles.Remove(SelectedProfile);
                SelectedProfile = null;
                _logger.LogInformation("Deleted host profile");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete host profile");
                ErrorMessage = $"Failed to delete profile: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Closes the dialog.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke();
    }

    private bool CanEditOrDelete() => SelectedProfile != null;

    partial void OnSelectedProfileChanged(HostProfile? value)
    {
        EditProfileCommand.NotifyCanExecuteChanged();
        DeleteProfileCommand.NotifyCanExecuteChanged();
    }
}
