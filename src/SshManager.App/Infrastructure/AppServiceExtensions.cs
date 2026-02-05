using Microsoft.Extensions.DependencyInjection;
using SshManager.App.Services;
using SshManager.App.Services.Validation;
using SshManager.App.ViewModels;
using SshManager.App.Views.Windows;
using Wpf.Ui;
#if DEBUG
using SshManager.App.Services.Testing;
#endif

namespace SshManager.App.Infrastructure;

/// <summary>
/// Extension methods for registering application-level services (WPF-UI, app services, ViewModels, Windows).
/// </summary>
public static class AppServiceExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        // WPF-UI services
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();

        // Core app services
        services.AddSingleton<IExternalTerminalService, ExternalTerminalService>();
        services.AddSingleton<IEditorThemeService, EditorThemeService>();
        services.AddSingleton<IRemoteFileEditorService, RemoteFileEditorService>();
        services.AddSingleton<IExportImportService, ExportImportService>();
        services.AddSingleton<ISshConfigParser, SshConfigParser>();
        services.AddSingleton<IPuttySessionImporter, PuttySessionImporter>();
        services.AddSingleton<ISystemTrayService, SystemTrayService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IPaneLayoutManager, PaneLayoutManager>();
        services.AddSingleton<ISessionConnectionService, SessionConnectionService>();
        services.AddSingleton<IAppThemeService, AppThemeService>();
        services.AddSingleton<IKeyboardShortcutHandler, KeyboardShortcutHandler>();
        services.AddSingleton<IWindowStateManager, WindowStateManager>();
        services.AddSingleton<IPaneOrchestrator, PaneOrchestrator>();

        // Cloud sync services
        services.AddSingleton<IOneDrivePathDetector, OneDrivePathDetector>();
        services.AddSingleton<ISyncConflictResolver, SyncConflictResolver>();
        services.AddSingleton<ICloudSyncService, CloudSyncService>();

        // Host status monitoring
        services.AddSingleton<IHostStatusService, HostStatusService>();

        // Validation services
        services.AddSingleton<IHostValidationService, HostValidationService>();

        // Session state monitoring
        services.AddSingleton<ISessionStateService, SessionStateService>();

        // ViewModels
        services.AddSingleton<HostManagementViewModel>();
        services.AddSingleton<SessionViewModel>();
        services.AddSingleton<SessionLoggingViewModel>();
        services.AddSingleton<BroadcastInputViewModel>();
        services.AddSingleton<SftpLauncherViewModel>();
        services.AddSingleton<ImportExportViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<PortForwardingManagerViewModel>();
        services.AddTransient<ProxyJumpProfileDialogViewModel>();
        services.AddTransient<PortForwardingProfileDialogViewModel>();
        services.AddSingleton<RecordingBrowserViewModel>();
        services.AddTransient<RecordingPlaybackViewModel>();
        services.AddTransient<TunnelBuilderViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();

#if DEBUG
        // Test automation services (DEBUG only)
        services.AddSingleton<ITestCommandHandler, TestCommandHandler>();
        services.AddSingleton<ITestServer, TestServer>();
#endif

        return services;
    }
}
