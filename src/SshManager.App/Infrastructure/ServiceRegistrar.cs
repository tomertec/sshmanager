using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SshManager.App.Services;
using SshManager.App.ViewModels;
using SshManager.App.Views.Windows;
using SshManager.Data;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal;
using SshManager.Terminal.Services;
using SshManager.Terminal.Services.Playback;
using SshManager.Terminal.Services.Recording;
using Wpf.Ui;

namespace SshManager.App.Infrastructure;

public static class ServiceRegistrar
{
    public static IServiceCollection AddSshManagerServices(this IServiceCollection services)
    {
        // Database
        services.AddDbContextFactory<AppDbContext>(options =>
        {
            var dbPath = DbPaths.GetDbPath();
            options.UseSqlite($"Data Source={dbPath}");
        });

        // Repositories
        services.AddSingleton<IHostRepository, HostRepository>();
        services.AddSingleton<IGroupRepository, GroupRepository>();
        services.AddSingleton<IHostProfileRepository, HostProfileRepository>();
        services.AddSingleton<IConnectionHistoryRepository, ConnectionHistoryRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<ISnippetRepository, SnippetRepository>();
        services.AddSingleton<IHostFingerprintRepository, HostFingerprintRepository>();
        services.AddSingleton<IManagedKeyRepository, ManagedKeyRepository>();
        services.AddSingleton<IProxyJumpProfileRepository, ProxyJumpProfileRepository>();
        services.AddSingleton<IPortForwardingProfileRepository, PortForwardingProfileRepository>();
        services.AddSingleton<ITagRepository, TagRepository>();
        services.AddSingleton<IHostEnvironmentVariableRepository, HostEnvironmentVariableRepository>();
        services.AddSingleton<ISessionRecordingRepository, SessionRecordingRepository>();

        // Security
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton<ICredentialCache, SecureCredentialCache>();
        services.AddSingleton<ISshKeyManager, SshKeyManagerService>();
        services.AddSingleton<IPpkConverter, PpkConverter>();
        services.AddSingleton<IPassphraseEncryptionService, PassphraseEncryptionService>();
        services.AddSingleton<IKeyEncryptionService, KeyEncryptionService>();

        // Session state monitoring
        services.AddSingleton<ISessionStateService, SessionStateService>();

        // Terminal services
        services.AddSingleton<ITerminalResizeService, TerminalResizeService>();
        services.AddSingleton<IConnectionRetryPolicy, ConnectionRetryPolicy>();
        services.AddSingleton<ISshAuthenticationFactory, SshAuthenticationFactory>();
        services.AddSingleton<IAgentDiagnosticsService, AgentDiagnosticsService>();
        services.AddSingleton<IAgentKeyService, AgentKeyService>();
        services.AddSingleton<IProxyChainConnectionBuilder, ProxyChainConnectionBuilder>();
        services.AddSingleton<ISshConnectionService, SshConnectionService>();
        services.AddSingleton<ISftpService, SftpService>();
        services.AddSingleton<ITerminalSessionManager, TerminalSessionManager>();
        services.AddSingleton<ISessionLoggingService, SessionLoggingService>();
        services.AddSingleton<IBroadcastInputService, BroadcastInputService>();
        services.AddSingleton<IServerStatsService, ServerStatsService>();
        services.AddSingleton<IProxyJumpService, ProxyJumpService>();
        services.AddSingleton<IPortForwardingService, PortForwardingService>();
        services.AddSingleton<ITerminalThemeService, TerminalThemeService>();
        services.AddSingleton<ITerminalFocusTracker, TerminalFocusTracker>();
        services.AddSingleton<ISessionRecordingService, SessionRecordingService>();
        services.AddSingleton<ISessionPlaybackService, SessionPlaybackService>();
        services.AddSingleton<ISerialConnectionService, SerialConnectionService>();

        // Phase 2: Terminal control extracted services (transient - per-control instances)
        services.AddTransient<ITerminalClipboardService, TerminalClipboardService>();
        services.AddTransient<ITerminalKeyboardHandler, TerminalKeyboardHandler>();
        services.AddTransient<ITerminalConnectionHandler, TerminalConnectionHandler>();

        // WPF-UI services
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();

        // App services
        services.AddSingleton<IExternalTerminalService, ExternalTerminalService>();
        services.AddSingleton<IEditorThemeService, EditorThemeService>();
        services.AddSingleton<IRemoteFileEditorService, RemoteFileEditorService>();
        services.AddSingleton<IExportImportService, ExportImportService>();
        services.AddSingleton<ISshConfigParser, SshConfigParser>();
        services.AddSingleton<ISshConfigExportService, SshConfigExportService>();
        services.AddSingleton<IPuttySessionImporter, PuttySessionImporter>();
        services.AddSingleton<ISystemTrayService, SystemTrayService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IPaneLayoutManager, PaneLayoutManager>();
        services.AddSingleton<ISessionConnectionService, SessionConnectionService>();

        // Cloud sync services
        services.AddSingleton<IOneDrivePathDetector, OneDrivePathDetector>();
        services.AddSingleton<ISyncConflictResolver, SyncConflictResolver>();
        services.AddSingleton<ICloudSyncService, CloudSyncService>();
        services.AddSingleton<CloudSyncHostedService>();

        // Host status monitoring
        services.AddSingleton<IHostStatusService, HostStatusService>();

        // Background services
        services.AddSingleton<AutoBackupHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<AutoBackupHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<CloudSyncHostedService>());
        services.AddHostedService<HostStatusHostedService>();

        // Background service health aggregation
        services.AddSingleton<IBackgroundServiceHealth>(sp => sp.GetRequiredService<AutoBackupHostedService>());
        services.AddSingleton<IBackgroundServiceHealthAggregator, BackgroundServiceHealthAggregator>();

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

        // Windows
        services.AddSingleton<MainWindow>();

        return services;
    }
}
