# SSH Agent Diagnostics Service - Usage Examples

## Overview

The `IAgentDiagnosticsService` provides visibility into SSH agent availability and loaded keys. It supports both Pageant and OpenSSH Agent on Windows.

## Basic Usage

### 1. Check Agent Availability (Quick Properties)

```csharp
// Inject the service (already registered in DI)
private readonly IAgentDiagnosticsService _agentDiagnostics;

public MyService(IAgentDiagnosticsService agentDiagnostics)
{
    _agentDiagnostics = agentDiagnostics;
}

public async Task CheckAgentStatus()
{
    // Trigger initial scan (first call)
    await _agentDiagnostics.GetDiagnosticsAsync();
    
    // Now use quick properties (cached)
    if (_agentDiagnostics.IsPageantAvailable)
    {
        Console.WriteLine($"Pageant is available with {_agentDiagnostics.AvailableKeyCount} keys");
    }
    else if (_agentDiagnostics.IsOpenSshAgentAvailable)
    {
        Console.WriteLine($"OpenSSH Agent is available with {_agentDiagnostics.AvailableKeyCount} keys");
    }
    else
    {
        Console.WriteLine("No SSH agent is available");
    }
}
```

### 2. Get Detailed Diagnostics

```csharp
public async Task ShowDetailedDiagnostics()
{
    var diagnostics = await _agentDiagnostics.GetDiagnosticsAsync();
    
    Console.WriteLine($"Pageant Available: {diagnostics.PageantAvailable}");
    Console.WriteLine($"OpenSSH Agent Available: {diagnostics.OpenSshAgentAvailable}");
    Console.WriteLine($"Active Agent: {diagnostics.ActiveAgentType ?? "None"}");
    Console.WriteLine($"Total Keys: {diagnostics.Keys.Count}");
    
    if (diagnostics.ErrorMessage != null)
    {
        Console.WriteLine($"Error: {diagnostics.ErrorMessage}");
    }
    
    // Display key information
    foreach (var key in diagnostics.Keys)
    {
        Console.WriteLine($"  Key Type: {key.KeyType}");
        Console.WriteLine($"  Fingerprint: {key.Fingerprint}");
        Console.WriteLine($"  Key Size: {key.KeySizeBits} bits");
        Console.WriteLine($"  Comment: {key.Comment ?? "(none)"}");
        Console.WriteLine();
    }
}
```

### 3. Refresh Agent Status

```csharp
public async Task RefreshAndCheck()
{
    // User just loaded keys into agent - refresh the cache
    await _agentDiagnostics.RefreshAsync();
    
    // Now get updated diagnostics
    var diagnostics = await _agentDiagnostics.GetDiagnosticsAsync();
    
    Console.WriteLine($"Updated: {diagnostics.Keys.Count} keys found");
}
```

### 4. Display User-Friendly Status

```csharp
public async Task<string> GetUserFriendlyStatus()
{
    var diagnostics = await _agentDiagnostics.GetDiagnosticsAsync();
    
    if (diagnostics.ErrorMessage != null)
    {
        return $"⚠️ {diagnostics.ErrorMessage}";
    }
    
    if (diagnostics.ActiveAgentType != null)
    {
        return $"✅ {diagnostics.ActiveAgentType}: {diagnostics.Keys.Count} key(s) loaded";
    }
    
    return "❌ No SSH agent running";
}
```

## Integration Example: Connection Dialog

```csharp
public class HostEditDialogViewModel
{
    private readonly IAgentDiagnosticsService _agentDiagnostics;
    
    public async Task OnAuthTypeChanged(AuthType authType)
    {
        if (authType == AuthType.SshAgent)
        {
            // Check if agent is available
            await _agentDiagnostics.RefreshAsync();
            
            if (_agentDiagnostics.AvailableKeyCount == 0)
            {
                // Show warning to user
                ShowWarning("No SSH keys are loaded in your SSH agent. " +
                           "Please add keys to Pageant or start OpenSSH Agent service.");
            }
            else
            {
                ShowInfo($"SSH Agent detected: {_agentDiagnostics.AvailableKeyCount} key(s) available " +
                        $"from {_agentDiagnostics.ActiveAgentType}");
            }
        }
    }
}
```

## Key Features

- **Caching**: Results are cached until `RefreshAsync()` is called
- **Thread-Safe**: Uses `SemaphoreSlim` for concurrent access
- **Agent Priority**: Pageant is checked first (matching `SshAuthenticationFactory` behavior)
- **Detailed Key Info**: Provides key type, fingerprint, size, and comment (when available)
- **Error Handling**: Gracefully handles agent unavailability

## Notes

- **Fingerprint Limitation**: Currently returns key type name instead of actual SHA-256 fingerprint due to SSH.NET library API limitations
- **Comment Limitation**: Key comments are not exposed by the `IPrivateKeySource` interface
- **Key Size**: For RSA keys, returns default 2048 bits (actual size extraction requires public key data access)
