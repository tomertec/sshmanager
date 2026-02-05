using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;

namespace SshManager.App.ViewModels.HostEdit;

/// <summary>
/// ViewModel for host metadata properties in the host edit dialog.
/// Contains display name, notes, secure notes, group selection, and tags.
/// </summary>
public partial class HostMetadataViewModel : ObservableObject
{
    private readonly ISecretProtector _secretProtector;
    private readonly ITagRepository? _tagRepo;
    private readonly ILogger<HostMetadataViewModel> _logger;

    // Store original host for loading tags by host ID
    private HostEntry? _originalHost;

    #region Display Name and Notes Properties

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string? _notes;

    [ObservableProperty]
    private string _secureNotes = string.Empty;

    [ObservableProperty]
    private bool _showSecureNotes;

    /// <summary>
    /// Gets or sets the displayed secure notes (masked when hidden, actual content when shown).
    /// </summary>
    public string DisplayedSecureNotes
    {
        get => ShowSecureNotes ? SecureNotes : (string.IsNullOrEmpty(SecureNotes) ? string.Empty : new string('\u2022', Math.Min(SecureNotes.Length, 20)));
        set
        {
            if (ShowSecureNotes)
            {
                SecureNotes = value;
                OnPropertyChanged();
            }
        }
    }

    #endregion

    #region Group Properties

    [ObservableProperty]
    private HostGroup? _selectedGroup;

    [ObservableProperty]
    private ObservableCollection<HostGroup> _availableGroups = [];

    #endregion

    #region Tag Properties

    [ObservableProperty]
    private ObservableCollection<Tag> _allTags = [];

    [ObservableProperty]
    private ObservableCollection<Tag> _selectedTags = [];

    [ObservableProperty]
    private string _newTagName = "";

    #endregion

    /// <summary>
    /// Creates a new instance of the HostMetadataViewModel.
    /// </summary>
    /// <param name="secretProtector">Service for secure notes encryption/decryption.</param>
    /// <param name="tagRepo">Optional tag repository for tag management.</param>
    /// <param name="host">Optional host entry to load settings from.</param>
    /// <param name="groups">Optional available groups for selection.</param>
    /// <param name="logger">Optional logger.</param>
    public HostMetadataViewModel(
        ISecretProtector secretProtector,
        ITagRepository? tagRepo = null,
        HostEntry? host = null,
        IEnumerable<HostGroup>? groups = null,
        ILogger<HostMetadataViewModel>? logger = null)
    {
        _secretProtector = secretProtector;
        _tagRepo = tagRepo;
        _logger = logger ?? NullLogger<HostMetadataViewModel>.Instance;

        // Set available groups
        if (groups != null)
        {
            AvailableGroups = new ObservableCollection<HostGroup>(groups);
        }

        // Load settings from host if provided
        if (host != null)
        {
            LoadFromHost(host);
        }
    }

    #region Load/Populate Methods

    /// <summary>
    /// Loads metadata settings from a HostEntry.
    /// </summary>
    /// <param name="host">The host entry to load settings from.</param>
    public void LoadFromHost(HostEntry host)
    {
        _originalHost = host;

        // Display name and notes
        DisplayName = host.DisplayName;
        Notes = host.Notes;

        // Decrypt secure notes if available
        if (!string.IsNullOrEmpty(host.SecureNotesProtected))
        {
            SecureNotes = _secretProtector.TryUnprotect(host.SecureNotesProtected) ?? "";
        }
        else
        {
            SecureNotes = "";
        }

        // Always hide secure notes initially
        ShowSecureNotes = false;

        // Find the matching group if exists
        if (host.GroupId.HasValue)
        {
            SelectedGroup = AvailableGroups.FirstOrDefault(g => g.Id == host.GroupId.Value);
        }
        else
        {
            SelectedGroup = null;
        }

        // Load selected tags from host
        if (host.Tags != null && host.Tags.Any())
        {
            SelectedTags = new ObservableCollection<Tag>(host.Tags);
        }
        else
        {
            SelectedTags = [];
        }

        _logger.LogDebug("Loaded metadata settings from host {HostId}", host.Id);
    }

    /// <summary>
    /// Populates a HostEntry with the current metadata settings.
    /// </summary>
    /// <param name="host">The host entry to populate.</param>
    public void PopulateHost(HostEntry host)
    {
        // Display name (use hostname if empty)
        host.DisplayName = string.IsNullOrWhiteSpace(DisplayName)
            ? host.Hostname // Fallback to hostname from SSH settings
            : DisplayName.Trim();

        // Notes
        host.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();

        // Encrypt secure notes if provided
        if (!string.IsNullOrEmpty(SecureNotes))
        {
            host.SecureNotesProtected = _secretProtector.Protect(SecureNotes);
        }
        else
        {
            host.SecureNotesProtected = null; // Clear if empty
        }

        // Group
        host.GroupId = SelectedGroup?.Id;
        host.Group = SelectedGroup;

        // Tags
        host.Tags = SelectedTags.ToList();

        _logger.LogDebug("Populated host with metadata settings");
    }

    #endregion

    #region Async Load Methods

    /// <summary>
    /// Loads all async data (tags).
    /// Call this after constructing the ViewModel.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        await LoadTagsAsync(ct);
    }

    /// <summary>
    /// Loads available tags asynchronously.
    /// Call this after constructing the ViewModel.
    /// </summary>
    public async Task LoadTagsAsync(CancellationToken ct = default)
    {
        if (_tagRepo == null) return;

        try
        {
            var tags = await _tagRepo.GetAllAsync(ct);
            AllTags = new ObservableCollection<Tag>(tags);

            // Preserve selected tags from host if already loaded
            // (SelectedTags may have been set in LoadFromHost)
            if (_originalHost?.Tags != null && _originalHost.Tags.Any() && SelectedTags.Count == 0)
            {
                SelectedTags = new ObservableCollection<Tag>(_originalHost.Tags);
            }

            _logger.LogDebug("Loaded {TagCount} available tags, {SelectedCount} selected", tags.Count, SelectedTags.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load tags");
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Toggles the visibility of secure notes (show/hide sensitive content).
    /// </summary>
    [RelayCommand]
    private void ToggleSecureNotesVisibility()
    {
        ShowSecureNotes = !ShowSecureNotes;
    }

    /// <summary>
    /// Creates a new tag using the NewTagName property.
    /// </summary>
    [RelayCommand]
    private async Task CreateTagAsync()
    {
        if (_tagRepo == null || string.IsNullOrWhiteSpace(NewTagName)) return;

        try
        {
            var tag = await _tagRepo.GetOrCreateAsync(NewTagName.Trim());

            // Add to all tags if not already present
            if (!AllTags.Any(t => t.Id == tag.Id))
            {
                AllTags.Add(tag);
            }

            // Add to selected tags if not already selected
            if (!SelectedTags.Any(t => t.Id == tag.Id))
            {
                SelectedTags.Add(tag);
            }

            _logger.LogInformation("Created/added tag: {TagName}", tag.Name);
            NewTagName = ""; // Clear the input field
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create tag: {TagName}", NewTagName);
        }
    }

    /// <summary>
    /// Toggles a tag's selection state for this host.
    /// </summary>
    [RelayCommand]
    private void ToggleTag(Tag tag)
    {
        var existingTag = SelectedTags.FirstOrDefault(t => t.Id == tag.Id);
        if (existingTag != null)
        {
            SelectedTags.Remove(existingTag);
            _logger.LogDebug("Removed tag: {TagName}", tag.Name);
        }
        else
        {
            SelectedTags.Add(tag);
            _logger.LogDebug("Added tag: {TagName}", tag.Name);
        }
    }

    #endregion

    #region Property Changed Handlers

    partial void OnShowSecureNotesChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayedSecureNotes));
    }

    partial void OnSecureNotesChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayedSecureNotes));
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets the IDs of all currently selected tags.
    /// </summary>
    /// <returns>Collection of selected tag IDs.</returns>
    public IEnumerable<Guid> GetSelectedTagIds()
    {
        return SelectedTags.Select(t => t.Id);
    }

    /// <summary>
    /// Checks if a tag is currently selected.
    /// </summary>
    /// <param name="tagId">The tag ID to check.</param>
    /// <returns>True if the tag is selected, false otherwise.</returns>
    public bool IsTagSelected(Guid tagId)
    {
        return SelectedTags.Any(t => t.Id == tagId);
    }

    /// <summary>
    /// Sets the available groups collection.
    /// </summary>
    /// <param name="groups">The groups to make available for selection.</param>
    public void SetAvailableGroups(IEnumerable<HostGroup> groups)
    {
        AvailableGroups = new ObservableCollection<HostGroup>(groups);

        // Re-select the group if we have a host with a group ID
        if (_originalHost?.GroupId.HasValue == true)
        {
            SelectedGroup = AvailableGroups.FirstOrDefault(g => g.Id == _originalHost.GroupId.Value);
        }
    }

    #endregion
}
