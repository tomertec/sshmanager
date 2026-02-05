namespace SshManager.Security;

/// <summary>
/// Internal constants for the Security module.
/// These values are used for encryption, DPAPI, and credential caching.
/// </summary>
internal static class SecurityConstants
{
    /// <summary>
    /// DPAPI encryption constants.
    /// </summary>
    public static class Dpapi
    {
        public const string EntropyString = "SshManager::v1::2024";
    }

    /// <summary>
    /// Passphrase encryption constants using Argon2 and AES-GCM.
    /// </summary>
    public static class PassphraseEncryption
    {
        public const int SaltSize = 16;           // 128 bits
        public const int KeySize = 32;            // 256 bits
        public const int NonceSize = 12;          // 96 bits (GCM standard)
        public const int TagSize = 16;            // 128 bits
        public const int DefaultMemorySize = 65536;  // 64 MB
        public const int DefaultIterations = 3;
        public const int DefaultParallelism = 4;
    }

    /// <summary>
    /// Secure credential cache constants.
    /// </summary>
    public static class CredentialCache
    {
        public const int CleanupIntervalSeconds = 60;
        public const int DefaultTimeoutMinutes = 15;
    }
}
