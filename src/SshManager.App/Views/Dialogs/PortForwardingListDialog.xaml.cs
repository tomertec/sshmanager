using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.App.ViewModels;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class PortForwardingListDialog : FluentWindow
{
    private readonly PortForwardingManagerViewModel _viewModel;
    private readonly IPortForwardingProfileRepository _repository;
    private readonly IReadOnlyList<HostEntry> _availableHosts;
    private readonly ILogger<PortForwardingListDialog> _logger;

    public PortForwardingListDialog(
        PortForwardingManagerViewModel viewModel,
        IPortForwardingProfileRepository repository,
        IReadOnlyList<HostEntry> availableHosts,
        ILogger<PortForwardingListDialog>? logger = null)
    {
        _viewModel = viewModel;
        _repository = repository;
        _availableHosts = availableHosts;
        _logger = logger ?? NullLogger<PortForwardingListDialog>.Instance;

        DataContext = viewModel;

        InitializeComponent();

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LoadProfilesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PortForwardingListDialog.OnLoaded");
        }
    }

    private async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialogViewModel = new PortForwardingProfileDialogViewModel(_repository);
            var dialog = new PortForwardingProfileDialog(dialogViewModel)
            {
                Owner = this
            };

            dialog.LoadAvailableHosts(_availableHosts);

            if (dialog.ShowDialog() == true)
            {
                await _viewModel.LoadProfilesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PortForwardingListDialog.AddProfile_Click");
        }
    }

    private async void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: PortForwardingProfile profile })
                return;

            var dialogViewModel = new PortForwardingProfileDialogViewModel(_repository, profile);
            var dialog = new PortForwardingProfileDialog(dialogViewModel)
            {
                Owner = this
            };

            dialog.LoadAvailableHosts(_availableHosts);

            if (dialog.ShowDialog() == true)
            {
                await _viewModel.LoadProfilesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PortForwardingListDialog.EditProfile_Click");
        }
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: PortForwardingProfile profile })
                return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete the profile '{profile.DisplayName}'?\n\nThis action cannot be undone.",
                "Delete Port Forwarding Profile",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                await _viewModel.DeleteProfileCommand.ExecuteAsync(profile);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PortForwardingListDialog.DeleteProfile_Click");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
