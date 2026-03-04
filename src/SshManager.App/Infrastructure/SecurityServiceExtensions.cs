using Microsoft.Extensions.DependencyInjection;
using SshManager.Security;
using SshManager.Security.OnePassword;

namespace SshManager.App.Infrastructure;

/// <summary>
/// Extension methods for registering security services (encryption, credentials, SSH key management).
/// </summary>
public static class SecurityServiceExtensions
{
    public static IServiceCollection AddSecurityServices(this IServiceCollection services)
    {
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton<ICredentialCache, SecureCredentialCache>();
        services.AddSingleton<ISshKeyManager, SshKeyManagerService>();
        services.AddSingleton<IPpkConverter, PpkConverter>();
        services.AddSingleton<IPassphraseEncryptionService, PassphraseEncryptionService>();
        services.AddSingleton<IKeyEncryptionService, KeyEncryptionService>();
        services.AddSingleton<IOnePasswordService, OnePasswordService>();

        return services;
    }
}
