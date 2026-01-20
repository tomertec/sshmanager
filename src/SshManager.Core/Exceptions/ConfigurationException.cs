namespace SshManager.Core.Exceptions;

/// <summary>
/// Exception thrown when there is a configuration error in SshManager.
/// </summary>
public class ConfigurationException : SshManagerException
{
    /// <summary>
    /// Gets the name of the setting or configuration that is invalid.
    /// </summary>
    public string? SettingName { get; }

    /// <summary>
    /// Gets the invalid value that was provided.
    /// </summary>
    public object? InvalidValue { get; }

    /// <summary>
    /// Creates a new ConfigurationException.
    /// </summary>
    /// <param name="message">Technical error message.</param>
    /// <param name="settingName">Name of the invalid setting.</param>
    /// <param name="invalidValue">The invalid value.</param>
    /// <param name="innerException">Optional inner exception.</param>
    public ConfigurationException(
        string message,
        string? settingName = null,
        object? invalidValue = null,
        Exception? innerException = null)
        : base(
            message,
            GetUserFriendlyMessage(message, settingName),
            "CONFIG_ERROR",
            innerException)
    {
        SettingName = settingName;
        InvalidValue = invalidValue;
    }

    private static string GetUserFriendlyMessage(string message, string? settingName)
    {
        if (!string.IsNullOrEmpty(settingName))
        {
            return $"Invalid configuration for '{settingName}'. {message}";
        }
        return $"Configuration error: {message}";
    }

    /// <summary>
    /// Creates a ConfigurationException for a missing required setting.
    /// </summary>
    public static ConfigurationException MissingRequired(string settingName)
    {
        return new ConfigurationException(
            $"Required setting '{settingName}' is missing or empty",
            settingName: settingName);
    }

    /// <summary>
    /// Creates a ConfigurationException for an invalid value.
    /// </summary>
    public static ConfigurationException ForInvalidValue(string settingName, object? value, string? reason = null)
    {
        var message = reason ?? $"Invalid value for '{settingName}'";
        return new ConfigurationException(
            message,
            settingName: settingName,
            invalidValue: value);
    }

    /// <summary>
    /// Creates a ConfigurationException for a value out of range.
    /// </summary>
    public static ConfigurationException OutOfRange(string settingName, object value, object min, object max)
    {
        return new ConfigurationException(
            $"Value {value} is out of range. Must be between {min} and {max}.",
            settingName: settingName,
            invalidValue: value);
    }
}
