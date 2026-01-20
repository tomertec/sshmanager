# Security Fixes for SSH Tunnel Builder

This document describes the critical security fixes applied to `TunnelBuilderService.cs` to address potential security vulnerabilities.

## Overview

Three critical security issues were identified and fixed in the SSH Tunnel Builder implementation:

1. **Insecure Bind Address Default** - Remote port forwarding defaulting to all interfaces
2. **Input Sanitization** - Command injection vulnerability in SSH command generation
3. **Port Number Validation** - Integer overflow risk when casting ports to uint

## Fixed Issues

### Issue 1: Insecure Bind Address Default (CRITICAL)

**Location:** Line 524 in `TunnelBuilderService.cs`

**Problem:**
```csharp
// BEFORE (VULNERABLE):
var boundHost = node.BindAddress ?? string.Empty; // Empty string defaults to all interfaces
```

When `BindAddress` was not specified for remote port forwarding, it defaulted to an empty string, which SSH.NET interprets as binding to all network interfaces (0.0.0.0). This could expose the forwarded port to the network unintentionally, creating a security risk.

**Fix:**
```csharp
// AFTER (SECURE):
// Security: Default to localhost (127.0.0.1) binding to prevent unintended network exposure.
// Empty string would bind to all interfaces (0.0.0.0), which is a security risk.
// Users must explicitly set BindAddress to "0.0.0.0" or a specific interface to bind externally.
var boundHost = node.BindAddress ?? "127.0.0.1";
```

**Security Impact:**
- **HIGH** - Prevents accidental exposure of tunneled services to the network
- Follows the principle of least privilege (default to most restrictive)
- Users must explicitly opt-in to bind to external interfaces

---

### Issue 2: Input Sanitization for SSH Command Generation

**Location:** Lines 288-295, 318-346 in `TunnelBuilderService.cs`

**Problem:**
```csharp
// BEFORE (VULNERABLE):
if (!string.IsNullOrEmpty(host.Username))
{
    sb.Append(host.Username);
    sb.Append('@');
}
sb.Append(host.Hostname);
```

Usernames and hostnames were inserted directly into SSH command strings without validation, allowing potential command injection attacks if these values contained shell metacharacters like semicolons, backticks, or dollar signs.

**Fix:**
```csharp
// AFTER (SECURE):
if (!string.IsNullOrEmpty(host.Username))
{
    // Security: Sanitize username to prevent command injection
    sb.Append(SanitizeSshIdentifier(host.Username));
    sb.Append('@');
}
// Security: Sanitize hostname to prevent command injection
sb.Append(SanitizeSshIdentifier(host.Hostname));
```

**New Security Method:**
```csharp
/// <summary>
/// Sanitizes SSH identifiers (usernames and hostnames) to prevent command injection.
/// </summary>
private static string SanitizeSshIdentifier(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return value;
    }

    // Allow only: alphanumeric, dot, hyphen, underscore, @ symbol, and IPv6 brackets
    // This prevents command injection via shell metacharacters
    foreach (var ch in value)
    {
        if (!char.IsLetterOrDigit(ch) &&
            ch != '.' &&
            ch != '-' &&
            ch != '_' &&
            ch != '@' &&
            ch != '[' &&  // IPv6 support
            ch != ']' &&  // IPv6 support
            ch != ':')    // IPv6 support
        {
            throw new ArgumentException(
                $"Invalid character '{ch}' detected in SSH identifier '{value}'. " +
                "Only alphanumeric characters, dots, hyphens, underscores, @ symbols, and IPv6 brackets are allowed.",
                nameof(value));
        }
    }

    return value;
}
```

**Security Impact:**
- **HIGH** - Prevents command injection attacks through malicious usernames/hostnames
- Validates against shell metacharacters: `;`, `|`, `&`, `$`, `` ` ``, `(`, `)`, `{`, `}`, `<`, `>`, etc.
- Allows legitimate characters including IPv6 addresses
- Fails fast with clear error messages

---

### Issue 3: Port Number Validation Before Cast

**Location:** Lines 494-495, 511-512, 538 in `TunnelBuilderService.cs`

**Problem:**
```csharp
// BEFORE (VULNERABLE):
forwardedPort = new ForwardedPortLocal(
    bindAddr,
    (uint)node.LocalPort.Value,  // Could overflow if negative or > int.MaxValue
    remoteHost,
    (uint)node.RemotePort.Value); // Could overflow if negative or > int.MaxValue
```

Port numbers were cast to `uint` without validation. If negative or out-of-range values were provided, the cast could produce unexpected results or cause integer overflow.

**Fix:**
```csharp
// AFTER (SECURE):
// Security: Validate port numbers before casting to uint (prevents overflow)
ValidatePortNumber(node.LocalPort.Value, "LocalPort");
ValidatePortNumber(node.RemotePort.Value, "RemotePort");

forwardedPort = new ForwardedPortLocal(
    bindAddr,
    (uint)node.LocalPort.Value,
    remoteHost,
    (uint)node.RemotePort.Value);
```

**New Security Method:**
```csharp
/// <summary>
/// Validates that a port number is within the valid range (1-65535) before casting to uint.
/// </summary>
/// <exception cref="ArgumentOutOfRangeException">Thrown when port is outside the valid range.</exception>
private static void ValidatePortNumber(int port, string parameterName)
{
    if (port < 1 || port > 65535)
    {
        throw new ArgumentOutOfRangeException(
            parameterName,
            port,
            $"Port number must be between 1 and 65535. Got: {port}");
    }
}
```

**Security Impact:**
- **MEDIUM** - Prevents integer overflow and unexpected behavior
- Ensures all ports are in valid TCP/UDP range (1-65535)
- Provides clear error messages for invalid ports
- Applied to all port forwarding types (Local, Remote, Dynamic)

---

## Testing Recommendations

### Test Case 1: Bind Address Security
- Create a tunnel with remote port forwarding
- Verify default bind address is 127.0.0.1 (not 0.0.0.0)
- Test explicit binding to external interfaces still works

### Test Case 2: SSH Command Injection Prevention
- Attempt to create hosts with malicious usernames/hostnames:
  - `user;whoami`
  - `user$(command)`
  - ``user`ls` ``
  - `user&malicious`
- Verify these are rejected with clear error messages

### Test Case 3: Port Validation
- Test negative port numbers: `-1`, `-8080`
- Test out-of-range ports: `0`, `65536`, `100000`
- Test valid edge cases: `1`, `65535`
- Verify appropriate exceptions are thrown

## Additional Security Enhancements

The codebase also includes these related security validations:

1. **Hostname/IP Validation** (Lines 362-395)
   - RFC 1123 compliant hostname validation
   - IPv4 and IPv6 address parsing
   - Maximum length and label validation

2. **TargetHost Validation** (Lines 687-690)
   - Validates RemoteHost format for TargetHost nodes
   - Ensures valid hostname or IP address format

## Files Modified

- `src/SshManager.Terminal/Services/TunnelBuilderService.cs`

## Backward Compatibility

### Breaking Changes:
1. **Remote port forwarding bind address** - Existing tunnels that relied on binding to all interfaces by default will now bind to localhost only
2. **Invalid hostnames/usernames** - Configurations with shell metacharacters will now be rejected

### Migration Guide:
- If you need remote port forwarding to bind to external interfaces, explicitly set `BindAddress` to `"0.0.0.0"` or a specific IP
- Ensure all hostnames and usernames only contain valid characters (alphanumeric, dots, hyphens, underscores, @ symbol)

## References

- **CWE-78**: Improper Neutralization of Special Elements used in an OS Command (Command Injection)
- **CWE-190**: Integer Overflow or Wraparound
- **RFC 1123**: Requirements for Internet Hosts -- Application and Support
- **OpenSSH ssh(1) man page**: SSH command-line options and security considerations
