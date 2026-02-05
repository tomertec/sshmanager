Detailed Refactoring Plan: HostDialogViewModel.cs
Overview
Refactor the 1109-line HostDialogViewModel.cs into smaller, focused ViewModels and a validation service using a composition pattern with direct property binding.
File Structure (After Refactoring)
src/SshManager.App/
├── ViewModels/
│   ├── HostEdit/                              # NEW FOLDER
│   │   ├── SshConnectionSettingsViewModel.cs  # ~280 lines
│   │   ├── SerialConnectionSettingsViewModel.cs # ~180 lines
│   │   ├── HostMetadataViewModel.cs           # ~200 lines
│   │   └── EnvironmentVariablesViewModel.cs   # ~120 lines
│   └── HostDialogViewModel.cs                 # ~350 lines (refactored)
├── Services/
│   ├── Validation/                            # NEW FOLDER
│   │   ├── IHostValidationService.cs          # ~30 lines
│   │   └── HostValidationService.cs           # ~150 lines
---
Step 1: Create Validation Service
1.1 Create IHostValidationService.cs
Location: src/SshManager.App/Services/Validation/IHostValidationService.cs
public interface IHostValidationService
{
    List<string> ValidateSshConnection(string hostname, int port, string username, 
        AuthType authType, string? privateKeyPath, string? password);
    List<string> ValidateSerialConnection(string? portName, int baudRate, int dataBits);
    bool IsValidHostname(string hostname);
    bool IsValidIpAddress(string ip);
}
1.2 Create HostValidationService.cs
Location: src/SshManager.App/Services/Validation/HostValidationService.cs
Move from HostDialogViewModel.cs:
- Lines 532-617: ValidateHost() method (split into SSH/Serial)
- Lines 619-631: IsValidHostname() and IsValidIpAddress() methods
- Lines 31-34: Regex patterns (HostnameRegex, IpAddressRegex, UsernameRegex)
1.3 Register in DI
Update: src/SshManager.App/Infrastructure/AppServiceExtensions.cs
services.AddSingleton<IHostValidationService, HostValidationService>();
---
Step 2: Create SerialConnectionSettingsViewModel.cs
Location: src/SshManager.App/ViewModels/HostEdit/SerialConnectionSettingsViewModel.cs
Properties to Move (~22 properties):
| Property | Source Lines |
|----------|-------------|
| AvailablePorts | 103 |
| SerialPortName | 107 |
| SerialBaudRate | 110 |
| SerialDataBits | 113 |
| SerialStopBits | 116 |
| SerialParity | 119 |
| SerialHandshake | 122 |
| SerialDtrEnable | 125 |
| SerialRtsEnable | 128 |
| SerialLocalEcho | 131 |
| SerialLineEnding | 134 |
| Static arrays: BaudRateOptions, DataBitsOptions, etc. | 179-184 |
Commands to Move:
| Command | Source Lines |
|---------|-------------|
| RefreshPortsCommand | 991-999 |
Dependencies:
- ISerialConnectionService - for GetAvailablePorts()
Constructor:
public SerialConnectionSettingsViewModel(ISerialConnectionService serialConnectionService)
---
Step 3: Create SshConnectionSettingsViewModel.cs
Location: src/SshManager.App/ViewModels/HostEdit/SshConnectionSettingsViewModel.cs
Properties to Move (~35 properties):
Basic Connection:
| Property | Source Lines |
|----------|-------------|
| Hostname | 40 |
| Port | 43 |
| Username | 46 |
| AuthType | 49 |
| ShellType | 52 |
| PrivateKeyPath | 55 |
| Password | 58 |
Host/Proxy Profiles:
| Property | Source Lines |
|----------|-------------|
| SelectedHostProfile | 193 |
| AvailableHostProfiles | 196 |
| SelectedProxyJumpProfile | 206 |
| AvailableProxyJumpProfiles | 209 |
| PortForwardingProfileCount | 212 |
Keep-Alive:
| Property | Source Lines |
|----------|-------------|
| UseGlobalKeepAliveSetting | 137 |
| KeepAliveIntervalSeconds | 140 |
X11 Forwarding:
| Property | Source Lines |
|----------|-------------|
| X11ForwardingEnabled | 144 |
| X11TrustedForwarding | 147 |
| X11DisplayNumber | 150 |
SSH Agent Status:
| Property | Source Lines |
|----------|-------------|
| IsAgentAvailable | 154 |
| AgentStatusText | 157 |
| IsCheckingAgent | 160 |
Kerberos:
| Property | Source Lines |
|----------|-------------|
| KerberosServicePrincipal | 164 |
| KerberosDelegateCredentials | 167 |
| IsKerberosAvailable | 170 |
| KerberosStatusText | 173 |
| IsCheckingKerberos | 176 |
Commands to Move:
| Command | Source Lines |
|---------|-------------|
| BrowsePrivateKeyCommand | 641-654 |
| ClearHostProfileCommand | 783-788 |
| ClearProxyJumpProfileCommand | 793-798 |
| ManageProxyJumpProfilesCommand | 765-769 |
| ManagePortForwardingCommand | 774-778 |
| RefreshAgentStatusAsyncCommand | 1004-1050 |
| RefreshKerberosStatusAsyncCommand | 1060-1107 |
Computed Properties:
| Property | Source Lines |
|----------|-------------|
| ShowPrivateKeyPath | 1052 |
| ShowPassword | 1053 |
| ShowAgentStatus | 1054 |
| ShowKerberosSettings | 1055 |
| PortForwardingStatusText | 926-931 |
Async Load Methods:
| Method | Source Lines |
|--------|-------------|
| LoadHostProfilesAsync() | 361-383 |
| LoadProxyJumpProfilesAsync() | 389-412 |
| LoadPortForwardingCountAsync() | 417-431 |
Events to Move:
| Event | Source Lines |
|-------|-------------|
| ManageProxyJumpProfilesRequested | 235 |
| ManagePortForwardingRequested | 240 |
Dependencies:
- ISecretProtector - password encryption
- IAgentDiagnosticsService? - SSH agent status
- IKerberosAuthService? - Kerberos status  
- IHostProfileRepository? - host profiles
- IProxyJumpProfileRepository? - proxy jump profiles
- IPortForwardingProfileRepository? - port forwarding profiles
---
Step 4: Create HostMetadataViewModel.cs
Location: src/SshManager.App/ViewModels/HostEdit/HostMetadataViewModel.cs
Properties to Move (~12 properties):
| Property | Source Lines |
|----------|-------------|
| DisplayName | 37 |
| Notes | 61 |
| SecureNotes | 64 |
| ShowSecureNotes | 67 |
| DisplayedSecureNotes (computed) | 72-83 |
| SelectedGroup | 187 |
| AvailableGroups | 190 |
| AllTags | 215 |
| SelectedTags | 218 |
| NewTagName | 221 |
Commands to Move:
| Command | Source Lines |
|---------|-------------|
| ToggleSecureNotesVisibilityCommand | 803-807 |
| CreateTagAsyncCommand | 812-840 |
| ToggleTagCommand | 845-859 |
Async Load Methods:
| Method | Source Lines |
|--------|-------------|
| LoadTagsAsync() | 437-458 |
Dependencies:
- ISecretProtector - secure notes encryption
- ITagRepository? - tag management
---
Step 5: Create EnvironmentVariablesViewModel.cs
Location: src/SshManager.App/ViewModels/HostEdit/EnvironmentVariablesViewModel.cs
Properties to Move (~3 properties):
| Property | Source Lines |
|----------|-------------|
| EnvironmentVariables | 224-225 |
| HasNoEnvironmentVariables (computed) | 230 |
Commands to Move:
| Command | Source Lines |
|---------|-------------|
| AddEnvironmentVariableCommand | 864-876 |
| RemoveEnvironmentVariableCommand | 881-889 |
| AddPresetEnvironmentVariableCommand | 894-921 |
Async Load Method:
| Method | Source Lines |
|--------|-------------|
| LoadEnvironmentVariablesAsync() | 476-498 |
Dependencies:
- IHostEnvironmentVariableRepository? - env var storage
---
Step 6: Refactor HostDialogViewModel.cs
Location: src/SshManager.App/ViewModels/HostDialogViewModel.cs (refactored)
Remaining Responsibilities (~350 lines):
1. Orchestration - Compose child ViewModels
2. Connection Type Toggle - SSH/Serial switching
3. Dialog Commands - Save, Cancel
4. Validation Coordination - Call IHostValidationService
5. GetHost() - Assemble HostEntry from child VMs
6. GetEnvironmentVariables() - Delegate to child VM
New Structure:
public partial class HostDialogViewModel : ObservableObject
{
    private readonly IHostValidationService _validationService;
    private readonly HostEntry _originalHost;
    
    // Child ViewModels (exposed for direct binding)
    public SshConnectionSettingsViewModel SshSettings { get; }
    public SerialConnectionSettingsViewModel SerialSettings { get; }
    public HostMetadataViewModel Metadata { get; }
    public EnvironmentVariablesViewModel EnvironmentVariables { get; }
    
    // Connection Type (stays here for radio button binding)
    [ObservableProperty] private bool _isSshConnection = true;
    [ObservableProperty] private bool _isSerialConnection;
    
    // Dialog State
    [ObservableProperty] private bool _isNewHost;
    [ObservableProperty] private string? _validationError;
    
    public string Title => IsNewHost ? "Add Host" : "Edit Host";
    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;
    
    // Commands
    [RelayCommand] private void Save() { ... }
    [RelayCommand] private void Cancel() { ... }
    
    // Assembly methods
    public HostEntry GetHost() { ... }
    public IEnumerable<HostEnvironmentVariable> GetEnvironmentVariables() => 
        EnvironmentVariables.GetEnvironmentVariables(_originalHost.Id);
}
Constructor:
public HostDialogViewModel(
    IHostValidationService validationService,
    ISecretProtector secretProtector,
    ISerialConnectionService serialConnectionService,
    IAgentDiagnosticsService? agentDiagnosticsService = null,
    IKerberosAuthService? kerberosAuthService = null,
    IHostProfileRepository? hostProfileRepo = null,
    IProxyJumpProfileRepository? proxyJumpRepo = null,
    IPortForwardingProfileRepository? portForwardingRepo = null,
    ITagRepository? tagRepo = null,
    IHostEnvironmentVariableRepository? envVarRepo = null,
    HostEntry? host = null,
    IEnumerable<HostGroup>? groups = null,
    ILogger<HostDialogViewModel>? logger = null)
{
    _validationService = validationService;
    _originalHost = host ?? new HostEntry();
    IsNewHost = host == null;
    
    // Initialize child ViewModels
    SshSettings = new SshConnectionSettingsViewModel(
        secretProtector, agentDiagnosticsService, kerberosAuthService,
        hostProfileRepo, proxyJumpRepo, portForwardingRepo, _originalHost);
        
    SerialSettings = new SerialConnectionSettingsViewModel(
        serialConnectionService, _originalHost);
        
    Metadata = new HostMetadataViewModel(
        secretProtector, tagRepo, _originalHost, groups);
        
    EnvironmentVariables = new EnvironmentVariablesViewModel(
        envVarRepo, _originalHost);
}
---
Step 7: Update XAML Bindings
7.1 Update HostEditDialog.xaml
<!-- Before -->
<RadioButton IsChecked="{Binding IsSshConnection}" />
<!-- After (unchanged - stays on parent) -->
<RadioButton IsChecked="{Binding IsSshConnection}" />
7.2 Update SshConnectionSection.xaml
<!-- Before -->
<ui:TextBox Text="{Binding Hostname}" />
<ComboBox SelectedItem="{Binding SelectedHostProfile}" />
<!-- After -->
<ui:TextBox Text="{Binding SshSettings.Hostname}" />
<ComboBox SelectedItem="{Binding SshSettings.SelectedHostProfile}" />
7.3 Update SerialConnectionSection.xaml
<!-- Before -->
<ComboBox SelectedItem="{Binding SerialPortName}" />
<ComboBox SelectedItem="{Binding SerialBaudRate}" />
<!-- After -->
<ComboBox SelectedItem="{Binding SerialSettings.SerialPortName}" />
<ComboBox SelectedItem="{Binding SerialSettings.SerialBaudRate}" />
7.4 Update AuthenticationSection.xaml
<!-- Before -->
<ComboBox SelectedItem="{Binding AuthType}" />
<ui:TextBox Text="{Binding PrivateKeyPath}" />
<Border Visibility="{Binding ShowAgentStatus, ...}" />
<!-- After -->
<ComboBox SelectedItem="{Binding SshSettings.AuthType}" />
<ui:TextBox Text="{Binding SshSettings.PrivateKeyPath}" />
<Border Visibility="{Binding SshSettings.ShowAgentStatus, ...}" />
7.5 Update GroupAndTagsSection.xaml
<!-- Before -->
<ComboBox SelectedItem="{Binding SelectedGroup}" />
<ItemsControl ItemsSource="{Binding AllTags}" />
<!-- After -->
<ComboBox SelectedItem="{Binding Metadata.SelectedGroup}" />
<ItemsControl ItemsSource="{Binding Metadata.AllTags}" />
7.6 Update NotesSection.xaml
<!-- Before -->
<TextBox Text="{Binding Notes}" />
<TextBox Text="{Binding DisplayedSecureNotes}" />
<!-- After -->
<TextBox Text="{Binding Metadata.Notes}" />
<TextBox Text="{Binding Metadata.DisplayedSecureNotes}" />
7.7 Update AdvancedOptionsSection.xaml
<!-- Before -->
<ComboBox SelectedItem="{Binding SelectedProxyJumpProfile}" />
<ItemsControl ItemsSource="{Binding EnvironmentVariables}" />
<ui:Button Command="{Binding AddEnvironmentVariableCommand}" />
<!-- After -->
<ComboBox SelectedItem="{Binding SshSettings.SelectedProxyJumpProfile}" />
<ItemsControl ItemsSource="{Binding EnvironmentVariables.Items}" />
<ui:Button Command="{Binding EnvironmentVariables.AddEnvironmentVariableCommand}" />
---
Step 8: Implementation Order
Phase 1: Infrastructure (~1 hour)
1. Create src/SshManager.App/Services/Validation/ folder
2. Create IHostValidationService.cs
3. Create HostValidationService.cs (move validation logic)
4. Register in DI
Phase 2: ViewModels (~2-3 hours)
1. Create src/SshManager.App/ViewModels/HostEdit/ folder
2. Create SerialConnectionSettingsViewModel.cs (simplest)
3. Create EnvironmentVariablesViewModel.cs
4. Create HostMetadataViewModel.cs
5. Create SshConnectionSettingsViewModel.cs (most complex)
Phase 3: Refactor Parent (~1 hour)
1. Update HostDialogViewModel.cs to compose child VMs
2. Remove migrated properties and commands
3. Update GetHost() to assemble from children
Phase 4: Update XAML (~1-2 hours)
1. Update SshConnectionSection.xaml bindings
2. Update SerialConnectionSection.xaml bindings
3. Update AuthenticationSection.xaml bindings
4. Update GroupAndTagsSection.xaml bindings
5. Update NotesSection.xaml bindings
6. Update AdvancedOptionsSection.xaml bindings
Phase 5: Test & Verify (~1 hour)
1. Build solution
2. Test Add Host dialog (SSH mode)
3. Test Add Host dialog (Serial mode)
4. Test Edit Host dialog
5. Verify all validation messages appear correctly
---
Summary
| Component | Estimated Lines | Complexity |
|-----------|----------------|------------|
| IHostValidationService.cs | ~30 | Low |
| HostValidationService.cs | ~150 | Medium |
| SerialConnectionSettingsViewModel.cs | ~180 | Low |
| EnvironmentVariablesViewModel.cs | ~120 | Low |
| HostMetadataViewModel.cs | ~200 | Medium |
| SshConnectionSettingsViewModel.cs | ~280 | High |
| HostDialogViewModel.cs (refactored) | ~350 | Medium |
| XAML updates | ~100 changes | Medium |
| Total | ~1310 lines | |
Benefits:
- Each ViewModel is focused on a single concern
- Easier to unit test validation logic independently
- XAML sections can be reused in other dialogs
- Clearer separation between SSH and Serial connection modes
- Environment variables can be extracted as a reusable component