using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SshManager.App.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

/// <summary>
/// Interaction logic for TunnelBuilderDialog.xaml
/// </summary>
public partial class TunnelBuilderDialog : FluentWindow
{
    private readonly TunnelBuilderViewModel _viewModel;
    private readonly ISnackbarService _snackbarService;

    /// <summary>
    /// Initializes a new instance of the TunnelBuilderDialog with dependency injection.
    /// </summary>
    /// <param name="viewModel">The view model for the tunnel builder.</param>
    /// <param name="snackbarService">The snackbar service for showing notifications.</param>
    public TunnelBuilderDialog(TunnelBuilderViewModel viewModel, ISnackbarService snackbarService)
    {
        _viewModel = viewModel;
        _snackbarService = snackbarService;
        DataContext = viewModel;

        InitializeComponent();

        // Set up dialog-local snackbar presenter
        _snackbarService.SetSnackbarPresenter(DialogSnackbarPresenter);

        // Subscribe to close request
        _viewModel.RequestClose += OnRequestClose;

        // Initialize on load
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Creates a new TunnelBuilderDialog instance with dependency injection.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    /// <param name="profileId">Optional profile ID to load an existing profile.</param>
    /// <returns>A configured TunnelBuilderDialog instance.</returns>
    public static async Task<TunnelBuilderDialog> CreateAsync(
        IServiceProvider serviceProvider,
        Guid? profileId = null)
    {
        var viewModel = serviceProvider.GetRequiredService<TunnelBuilderViewModel>();
        var snackbarService = serviceProvider.GetRequiredService<ISnackbarService>();
        var dialog = new TunnelBuilderDialog(viewModel, snackbarService);

        await dialog.InitializeDialogAsync(profileId);

        return dialog;
    }

    /// <summary>
    /// Initializes the dialog and optionally loads a profile.
    /// </summary>
    /// <param name="profileId">Optional profile ID to load.</param>
    private async Task InitializeDialogAsync(Guid? profileId)
    {
        await _viewModel.InitializeAsync();

        if (profileId.HasValue)
        {
            await _viewModel.LoadProfileAsync(profileId.Value);
        }
    }

    /// <summary>
    /// Handles the Loaded event to initialize the view model.
    /// </summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // If Initialize was not called via CreateAsync, initialize now
        if (_viewModel.AvailableHosts.Count == 0)
        {
            await _viewModel.InitializeAsync();
        }
    }

    /// <summary>
    /// Handles the RequestClose event from the view model.
    /// </summary>
    private void OnRequestClose()
    {
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Handles the Close button click.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Cleanup when the dialog is closed.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;

        // Restore snackbar presenter to the main window
        if (Owner is Views.Windows.MainWindow mainWindow)
        {
            // The main window will re-set its presenter on next snackbar call
            // But we need to ensure the presenter is properly restored
            var mainPresenter = mainWindow.FindName("SnackbarPresenter") as SnackbarPresenter;
            if (mainPresenter != null)
            {
                _snackbarService.SetSnackbarPresenter(mainPresenter);
            }
        }

        base.OnClosed(e);
    }
}
