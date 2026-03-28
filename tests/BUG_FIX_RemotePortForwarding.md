# Bug Fix: Remote Port Forwarding in TunnelBuilderService

## Problem Summary

Remote port forwarding was incorrectly using `BindAddress` as the forwarding target host instead of `RemoteHost`, and the `TargetHost` node type was being validated but never consumed during command generation or execution.

## Root Cause

The bug was caused by a misunderstanding of the SSH remote forwarding format:

```
-R [bind_address:]port:host:hostport
```

Where:
- `bind_address` - Interface on the REMOTE server to bind to (optional, defaults to loopback)
- `port` - Port on the REMOTE server to listen on
- `host` - Target host that connections will be forwarded TO (from perspective of LOCAL machine)
- `hostport` - Target port on the host

The code was incorrectly using `BindAddress` for the `host` parameter.

## Affected Code Locations

### 1. TunnelBuilderService.cs - GenerateSshCommand (Line 209-216)

**Before:**
```csharp
else if (node.NodeType == TunnelNodeType.RemotePort)
{
    // Remote forward: -R remote_port:local_host:local_port
    if (node.RemotePort.HasValue && node.LocalPort.HasValue)
    {
        var bindAddr = node.BindAddress ?? "localhost";
        sb.Append($" -R {node.RemotePort}:{bindAddr}:{node.LocalPort}");
    }
}
```

**After:**
```csharp
else if (node.NodeType == TunnelNodeType.RemotePort)
{
    // Remote forward: -R [bind_address:]remote_port:target_host:target_port
    // bind_address: interface on remote server to bind to (optional)
    // remote_port: port on remote server to listen on
    // target_host: where to forward connections to (from local machine's perspective)
    // target_port: port on the target host
    if (node.RemotePort.HasValue && node.LocalPort.HasValue)
    {
        var targetHost = GetTargetHostForRemoteForward(node, profile) ?? node.RemoteHost ?? "localhost";

        if (!string.IsNullOrWhiteSpace(node.BindAddress))
        {
            sb.Append($" -R {node.BindAddress}:{node.RemotePort}:{targetHost}:{node.LocalPort}");
        }
        else
        {
            sb.Append($" -R {node.RemotePort}:{targetHost}:{node.LocalPort}");
        }
    }
}
```

### 2. TunnelBuilderService.cs - ExecuteAsync (Line 361-370)

**Before:**
```csharp
else if (node.NodeType == TunnelNodeType.RemotePort)
{
    if (node.RemotePort.HasValue && node.LocalPort.HasValue)
    {
        var bindAddr = node.BindAddress ?? "127.0.0.1";
        forwardedPort = new ForwardedPortRemote(
            (uint)node.RemotePort.Value,
            bindAddr,
            (uint)node.LocalPort.Value);
    }
}
```

**After:**
```csharp
else if (node.NodeType == TunnelNodeType.RemotePort)
{
    if (node.RemotePort.HasValue && node.LocalPort.HasValue)
    {
        // ForwardedPortRemote constructor: (boundHost, boundPort, host, port)
        // boundHost: interface on remote server to bind to
        // boundPort: port on remote server to listen on
        // host: target host to forward connections to (from local machine's perspective)
        // port: target port on the host
        var targetHost = GetTargetHostForRemoteForward(node, profile) ?? node.RemoteHost ?? "127.0.0.1";
        var boundHost = node.BindAddress ?? string.Empty; // Empty string defaults to all interfaces

        forwardedPort = new ForwardedPortRemote(
            boundHost,
            (uint)node.RemotePort.Value,
            targetHost,
            (uint)node.LocalPort.Value);
    }
}
```

### 3. TunnelBuilderService.cs - New Helper Method (Line 243-276)

Added `GetTargetHostForRemoteForward` helper method to support connected `TargetHost` nodes:

```csharp
/// <summary>
/// Gets the target host for remote port forwarding by checking for connected TargetHost nodes.
/// </summary>
/// <param name="remotePortNode">The RemotePort node to find targets for.</param>
/// <param name="profile">The tunnel profile containing the graph.</param>
/// <returns>The target hostname from a connected TargetHost node, or null if none found.</returns>
private static string? GetTargetHostForRemoteForward(TunnelNode remotePortNode, TunnelProfile profile)
{
    // Find edges where this RemotePort node is the source
    var outgoingEdges = profile.Edges.Where(e => e.SourceNodeId == remotePortNode.Id);

    // Look for a connected TargetHost node
    foreach (var edge in outgoingEdges)
    {
        var targetNode = profile.Nodes.FirstOrDefault(n => n.Id == edge.TargetNodeId);
        if (targetNode?.NodeType == TunnelNodeType.TargetHost && !string.IsNullOrWhiteSpace(targetNode.RemoteHost))
        {
            return targetNode.RemoteHost;
        }
    }

    // Also check incoming edges (in case TargetHost is connected TO the RemotePort node)
    var incomingEdges = profile.Edges.Where(e => e.TargetNodeId == remotePortNode.Id);
    foreach (var edge in incomingEdges)
    {
        var sourceNode = profile.Nodes.FirstOrDefault(n => n.Id == edge.SourceNodeId);
        if (sourceNode?.NodeType == TunnelNodeType.TargetHost && !string.IsNullOrWhiteSpace(sourceNode.RemoteHost))
        {
            return sourceNode.RemoteHost;
        }
    }

    return null;
}
```

### 4. TunnelBuilderService.cs - Enhanced Validation (Line 570-594)

Enhanced validation for `RemotePort` nodes to check for `LocalPort` (target port) and provide clearer warnings:

```csharp
case TunnelNodeType.RemotePort:
    if (!node.RemotePort.HasValue)
    {
        errors.Add($"Remote port node '{node.Label}' must have a RemotePort.");
    }
    else if (node.RemotePort.Value < 1 || node.RemotePort.Value > 65535)
    {
        errors.Add($"Remote port node '{node.Label}' has invalid RemotePort: {node.RemotePort.Value}");
    }

    if (!node.LocalPort.HasValue)
    {
        errors.Add($"Remote port node '{node.Label}' must have a LocalPort (target port).");
    }
    else if (node.LocalPort.Value < 1 || node.LocalPort.Value > 65535)
    {
        errors.Add($"Remote port node '{node.Label}' has invalid LocalPort (target port): {node.LocalPort.Value}");
    }

    // Note: RemoteHost can be empty if a TargetHost node is connected instead
    if (string.IsNullOrWhiteSpace(node.RemoteHost))
    {
        warnings.Add($"Remote port node '{node.Label}' should specify a RemoteHost or connect to a TargetHost node (defaults to localhost).");
    }
    break;
```

### 5. TunnelNode.cs - Enhanced Documentation (Line 38-60)

Updated XML documentation to clarify the purpose of each property:

```csharp
/// <summary>
/// Local port number.
/// For LocalPort: the port on the local machine to listen on.
/// For RemotePort: the target port on the forwarding destination (confusingly named, but represents the target port).
/// For DynamicProxy: the local SOCKS proxy port to listen on.
/// </summary>
public int? LocalPort { get; set; }

/// <summary>
/// Remote hostname (for TargetHost and RemotePort node types).
/// For RemotePort: the target host where forwarded connections will be sent to.
/// For TargetHost: the destination hostname for the port forward.
/// </summary>
public string? RemoteHost { get; set; }

/// <summary>
/// Bind address for port forwarding.
/// For LocalPort: the interface on the local machine to bind to (default: "localhost").
/// For RemotePort: the interface on the remote server to bind to (default: loopback).
/// </summary>
public string? BindAddress { get; set; }
```

## Changes Summary

1. **Command Generation**: Now correctly uses `RemoteHost` (or connected `TargetHost`) as the forwarding target
2. **Execution**: Fixed `ForwardedPortRemote` constructor to use correct parameter order
3. **TargetHost Support**: Added support for connecting `TargetHost` nodes to `RemotePort` nodes
4. **Validation**: Enhanced to check for required `LocalPort` and provide clearer warnings
5. **Documentation**: Improved XML comments to clarify confusing property names

## Example Usage

### Before (Incorrect):
```bash
# RemotePort node with RemotePort=8080, BindAddress="internal-server", LocalPort=80
ssh -R 8080:internal-server:80 user@jumphost
# This would try to bind on jumphost:8080 and forward to internal-server:80
# But internal-server was meant to be the bind address!
```

### After (Correct):
```bash
# RemotePort node with RemotePort=8080, RemoteHost="internal-server", LocalPort=80
ssh -R 8080:internal-server:80 user@jumphost
# Now correctly: bind on jumphost:8080, forward to internal-server:80

# With BindAddress specified:
# RemotePort node with BindAddress="0.0.0.0", RemotePort=8080, RemoteHost="internal-server", LocalPort=80
ssh -R 0.0.0.0:8080:internal-server:80 user@jumphost
# Bind on all interfaces of jumphost:8080, forward to internal-server:80
```

## Testing Recommendations

1. Test remote forwarding with `RemoteHost` specified directly on the node
2. Test remote forwarding with a connected `TargetHost` node
3. Test remote forwarding with `BindAddress` specified (should bind to specific interface)
4. Test remote forwarding without `BindAddress` (should default to loopback)
5. Verify validation errors for missing `LocalPort` or `RemotePort`
6. Verify warning when neither `RemoteHost` nor connected `TargetHost` is specified

## Files Modified

- `src/SshManager.Terminal/Services/TunnelBuilderService.cs`
- `src/SshManager.Core/Models/TunnelNode.cs`

## Build Status

✅ Build succeeded with no warnings or errors
