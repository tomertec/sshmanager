using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the file/folder properties dialog.
/// Displays file information and allows editing permissions.
/// </summary>
public partial class FilePropertiesDialogViewModel : ObservableObject
{
    private readonly FileItemViewModel _item;
    private readonly ISftpSession? _session;
    private readonly int _originalPermissions;

    #region Display Properties

    /// <summary>
    /// Dialog title showing the file/folder name.
    /// </summary>
    public string DialogTitle => $"{_item.Name} Properties";

    /// <summary>
    /// The file or folder name.
    /// </summary>
    public string Name => _item.Name;

    /// <summary>
    /// The location (parent directory path).
    /// </summary>
    public string Location { get; }

    /// <summary>
    /// Whether this is a directory.
    /// </summary>
    public bool IsDirectory => _item.IsDirectory;

    /// <summary>
    /// Formatted file size display.
    /// </summary>
    public string SizeDisplay { get; }

    /// <summary>
    /// Whether checksum tab should be visible (files only).
    /// </summary>
    public bool ShowChecksumTab => !_item.IsDirectory;

    #endregion

    #region Owner/Group

    [ObservableProperty]
    private string _owner = "";

    [ObservableProperty]
    private string _group = "";

    #endregion

    #region Permission Checkboxes

    // Owner permissions
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OctalPermissions))]
    private bool _ownerRead;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OctalPermissions))]
    private bool _ownerWrite;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OctalPermissions))]
    private bool _ownerExecute;

    // Group permissions
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OctalPermissions))]
    private bool _groupRead;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OctalPermissions))]
    private bool _groupWrite;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OctalPermissions))]
    private bool _groupExecute;

    // Others permissions
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OctalPermissions))]
    private bool _othersRead;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OctalPermissions))]
    private bool _othersWrite;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OctalPermissions))]
    private bool _othersExecute;

    // Special bits
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OctalPermissions))]
    private bool _setUid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OctalPermissions))]
    private bool _setGid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OctalPermissions))]
    private bool _stickyBit;

    /// <summary>
    /// Octal permission string (e.g., "0755").
    /// </summary>
    public string OctalPermissions
    {
        get
        {
            var perms = CalculatePermissions();
            var special = (SetUid ? 4 : 0) | (SetGid ? 2 : 0) | (StickyBit ? 1 : 0);
            return special > 0 ? $"{special}{perms:D3}" : $"0{perms:D3}";
        }
        set
        {
            if (TryParseOctal(value, out var perms, out var special))
            {
                SetPermissionsFromInt(perms);
                SetUid = (special & 4) != 0;
                SetGid = (special & 2) != 0;
                StickyBit = (special & 1) != 0;
            }
        }
    }

    #endregion

    #region Checksum

    [ObservableProperty]
    private string _md5Checksum = "";

    [ObservableProperty]
    private string _sha256Checksum = "";

    [ObservableProperty]
    private bool _isCalculatingChecksum;

    [ObservableProperty]
    private string _checksumError = "";

    #endregion

    #region Dialog State

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string _errorMessage = "";

    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;

    /// <summary>
    /// Whether permissions have been modified.
    /// </summary>
    public bool HasChanges => CalculateFullPermissions() != _originalPermissions;

    #endregion

    public FilePropertiesDialogViewModel(FileItemViewModel item, ISftpSession? session)
    {
        _item = item;
        _session = session;
        _originalPermissions = item.Permissions ?? 0;

        // Set location (parent directory)
        var fullPath = item.FullPath;
        var lastSlash = fullPath.LastIndexOf('/');
        Location = lastSlash > 0 ? fullPath[..lastSlash] : "/";

        // Set size display
        SizeDisplay = item.IsDirectory ? "-" : FormatSize(item.Size);

        // Set owner/group
        Owner = item.Owner ?? "";
        Group = item.Group ?? "";

        // Initialize permissions from item
        if (item.Permissions.HasValue)
        {
            SetPermissionsFromInt(item.Permissions.Value);
        }
    }

    private void SetPermissionsFromInt(int permissions)
    {
        // Standard Unix permissions (9 bits)
        OwnerRead = (permissions & 0x100) != 0;
        OwnerWrite = (permissions & 0x080) != 0;
        OwnerExecute = (permissions & 0x040) != 0;

        GroupRead = (permissions & 0x020) != 0;
        GroupWrite = (permissions & 0x010) != 0;
        GroupExecute = (permissions & 0x008) != 0;

        OthersRead = (permissions & 0x004) != 0;
        OthersWrite = (permissions & 0x002) != 0;
        OthersExecute = (permissions & 0x001) != 0;
    }

    private int CalculatePermissions()
    {
        var owner = (OwnerRead ? 4 : 0) | (OwnerWrite ? 2 : 0) | (OwnerExecute ? 1 : 0);
        var group = (GroupRead ? 4 : 0) | (GroupWrite ? 2 : 0) | (GroupExecute ? 1 : 0);
        var others = (OthersRead ? 4 : 0) | (OthersWrite ? 2 : 0) | (OthersExecute ? 1 : 0);
        return (owner << 6) | (group << 3) | others;
    }

    private int CalculateFullPermissions()
    {
        var basic = CalculatePermissions();
        var special = (SetUid ? 0x800 : 0) | (SetGid ? 0x400 : 0) | (StickyBit ? 0x200 : 0);
        return basic | special;
    }

    private static bool TryParseOctal(string value, out int permissions, out int special)
    {
        permissions = 0;
        special = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Remove leading zeros and any non-digit chars
        value = value.Trim().TrimStart('0');
        if (string.IsNullOrEmpty(value))
        {
            permissions = 0;
            return true;
        }

        // Try to parse as octal
        try
        {
            var num = Convert.ToInt32(value, 8);
            if (value.Length == 4)
            {
                special = (num >> 9) & 7;
                permissions = num & 0x1FF;
            }
            else
            {
                permissions = num & 0x1FF;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes:N0} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:N2} KB ({bytes:N0} B)";
        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):N2} MB ({bytes:N0} B)";
        return $"{bytes / (1024.0 * 1024 * 1024):N2} GB ({bytes:N0} B)";
    }

    [RelayCommand]
    private async Task CalculateChecksumAsync()
    {
        if (_session == null || _item.IsDirectory)
            return;

        IsCalculatingChecksum = true;
        ChecksumError = "";
        Md5Checksum = "Calculating...";
        Sha256Checksum = "Calculating...";

        try
        {
            // Download file content to calculate checksums
            var content = await _session.ReadAllBytesAsync(_item.FullPath);

            // Calculate MD5
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(content);
                Md5Checksum = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }

            // Calculate SHA256
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(content);
                Sha256Checksum = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
        catch (Exception ex)
        {
            ChecksumError = $"Failed to calculate checksum: {ex.Message}";
            Md5Checksum = "";
            Sha256Checksum = "";
        }
        finally
        {
            IsCalculatingChecksum = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_session == null)
        {
            ErrorMessage = "No SFTP session available";
            return;
        }

        if (!HasChanges)
        {
            DialogResult = true;
            RequestClose?.Invoke();
            return;
        }

        IsSaving = true;
        ErrorMessage = "";

        try
        {
            var newPermissions = CalculateFullPermissions();
            await _session.ChangePermissionsAsync(_item.FullPath, newPermissions);

            // Update the item's permissions
            _item.Permissions = newPermissions;

            DialogResult = true;
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to change permissions: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Gets the new permissions value after editing.
    /// </summary>
    public int GetNewPermissions() => CalculateFullPermissions();
}
