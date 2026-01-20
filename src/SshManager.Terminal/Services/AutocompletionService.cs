using System.Text;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using SshManager.Core.Models;
using SshManager.Data.Repositories;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service for providing terminal autocompletion suggestions.
/// Supports remote shell completions, local command history, and hybrid mode.
/// </summary>
public sealed class AutocompletionService : IAutocompletionService
{
    private readonly ICommandHistoryRepository _commandHistoryRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<AutocompletionService> _logger;

    private const int MaxRemoteCompletions = 20;
    private const int MaxLocalCompletions = 15;
    private const int RemoteCompletionTimeoutMs = 1000;

    public AutocompletionService(
        ICommandHistoryRepository commandHistoryRepository,
        ISettingsRepository settingsRepository,
        ILogger<AutocompletionService> logger)
    {
        _commandHistoryRepository = commandHistoryRepository;
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
        ShellStream? shellStream,
        Guid? hostId,
        string currentLine,
        int cursorPosition,
        CancellationToken ct = default)
    {
        try
        {
            // Get settings to determine completion mode
            var settings = await _settingsRepository.GetAsync(ct);

            if (!settings.EnableAutocompletion)
            {
                return Array.Empty<CompletionItem>();
            }

            // Extract the word at cursor position
            var wordInfo = ExtractWordAtCursor(currentLine, cursorPosition);
            if (string.IsNullOrWhiteSpace(wordInfo.Word))
            {
                return Array.Empty<CompletionItem>();
            }

            return settings.AutocompletionMode switch
            {
                AutocompletionMode.RemoteShell => await GetRemoteCompletionsAsync(
                    shellStream, wordInfo, ct),
                AutocompletionMode.LocalHistory => await GetLocalCompletionsAsync(
                    hostId, wordInfo, ct),
                AutocompletionMode.Hybrid => await GetHybridCompletionsAsync(
                    shellStream, hostId, wordInfo, ct),
                _ => Array.Empty<CompletionItem>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting completions for line: {Line}", currentLine);
            return Array.Empty<CompletionItem>();
        }
    }

    /// <inheritdoc/>
    public async Task RecordCommandAsync(Guid? hostId, string command, CancellationToken ct = default)
    {
        try
        {
            // Only record non-empty commands
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            // Trim whitespace
            command = command.Trim();

            await _commandHistoryRepository.AddAsync(hostId, command, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording command: {Command}", command);
        }
    }

    /// <summary>
    /// Gets completions from remote shell using compgen.
    /// </summary>
    private async Task<IReadOnlyList<CompletionItem>> GetRemoteCompletionsAsync(
        ShellStream? shellStream,
        WordInfo wordInfo,
        CancellationToken ct)
    {
        if (shellStream == null || !shellStream.CanRead || !shellStream.CanWrite)
        {
            return Array.Empty<CompletionItem>();
        }

        try
        {
            // Determine completion type based on context
            var (compgenCommand, completionType) = DetermineCompletionCommand(wordInfo);

            // Execute compgen command with timeout
            var completions = await ExecuteRemoteCompletionAsync(
                shellStream, compgenCommand, completionType, ct);

            return completions;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Remote completion failed for word: {Word}", wordInfo.Word);
            return Array.Empty<CompletionItem>();
        }
    }

    /// <summary>
    /// Gets completions from local command history.
    /// </summary>
    private async Task<IReadOnlyList<CompletionItem>> GetLocalCompletionsAsync(
        Guid? hostId,
        WordInfo wordInfo,
        CancellationToken ct)
    {
        try
        {
            // Only complete if we're at the start (command completion)
            if (!wordInfo.IsFirstWord)
            {
                return Array.Empty<CompletionItem>();
            }

            var suggestions = await _commandHistoryRepository.GetSuggestionsAsync(
                hostId,
                wordInfo.Word,
                MaxLocalCompletions,
                ct);

            return suggestions
                .Select((s, index) => new CompletionItem
                {
                    InsertText = s.Command,
                    DisplayText = s.Command,
                    Type = CompletionItemType.History,
                    Score = CalculateHistoryScore(s, index)
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting local completions for word: {Word}", wordInfo.Word);
            return Array.Empty<CompletionItem>();
        }
    }

    /// <summary>
    /// Gets completions from both remote shell and local history, then merges and ranks them.
    /// </summary>
    private async Task<IReadOnlyList<CompletionItem>> GetHybridCompletionsAsync(
        ShellStream? shellStream,
        Guid? hostId,
        WordInfo wordInfo,
        CancellationToken ct)
    {
        try
        {
            // Get both sources in parallel
            var remoteTask = GetRemoteCompletionsAsync(shellStream, wordInfo, ct);
            var localTask = GetLocalCompletionsAsync(hostId, wordInfo, ct);

            await Task.WhenAll(remoteTask, localTask);

            var remoteCompletions = await remoteTask;
            var localCompletions = await localTask;

            // Merge and deduplicate
            var allCompletions = new Dictionary<string, CompletionItem>(StringComparer.OrdinalIgnoreCase);

            // Add remote completions first
            foreach (var item in remoteCompletions)
            {
                var key = item.InsertText ?? item.DisplayText;
                allCompletions[key] = item;
            }

            // Add local completions (prefer local if there's a conflict, as it has usage data)
            foreach (var item in localCompletions)
            {
                var key = item.InsertText ?? item.DisplayText;
                if (allCompletions.TryGetValue(key, out var existing))
                {
                    // Boost score for items that appear in both sources
                    allCompletions[key] = new CompletionItem
                    {
                        InsertText = item.InsertText,
                        DisplayText = item.DisplayText,
                        Type = item.Type,
                        Description = item.Description,
                        Score = item.Score + existing.Score
                    };
                }
                else
                {
                    allCompletions[key] = item;
                }
            }

            // Sort by score (descending) and return
            return allCompletions.Values
                .OrderByDescending(c => c.Score)
                .Take(MaxRemoteCompletions)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting hybrid completions for word: {Word}", wordInfo.Word);
            return Array.Empty<CompletionItem>();
        }
    }

    /// <summary>
    /// Executes a compgen command on the remote shell and parses the results.
    /// </summary>
    private async Task<IReadOnlyList<CompletionItem>> ExecuteRemoteCompletionAsync(
        ShellStream shellStream,
        string compgenCommand,
        CompletionItemType completionType,
        CancellationToken ct)
    {
        var completions = new List<CompletionItem>();

        try
        {
            // Clear any pending data
            if (shellStream.DataAvailable)
            {
                _ = shellStream.Read();
            }

            // Send the compgen command
            await shellStream.WriteAsync(Encoding.UTF8.GetBytes(compgenCommand + "\n"), ct);
            await shellStream.FlushAsync(ct);

            // Wait for output with timeout
            using var timeoutCts = new CancellationTokenSource(RemoteCompletionTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var output = await ReadUntilPromptAsync(shellStream, linkedCts.Token);

            // Parse output lines
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var line in lines)
            {
                // Skip the echo of our command
                if (line.Contains("compgen"))
                {
                    continue;
                }

                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // Add completion item
                completions.Add(new CompletionItem
                {
                    InsertText = line,
                    DisplayText = line,
                    Type = completionType,
                    Score = CalculateRemoteScore(line, completionType, completions.Count)
                });

                // Limit results
                if (completions.Count >= MaxRemoteCompletions)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Remote completion timed out");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error executing remote completion command: {Command}", compgenCommand);
        }

        return completions;
    }

    /// <summary>
    /// Reads from shell stream until we detect a prompt or timeout.
    /// </summary>
    private async Task<string> ReadUntilPromptAsync(ShellStream shellStream, CancellationToken ct)
    {
        var output = new StringBuilder();
        var buffer = new byte[4096];
        var lastReadTime = DateTime.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            if (shellStream.DataAvailable)
            {
                var bytesRead = await shellStream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (bytesRead > 0)
                {
                    output.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                    lastReadTime = DateTime.UtcNow;
                }
            }
            else
            {
                // If no data available for 100ms, assume prompt has returned
                if ((DateTime.UtcNow - lastReadTime).TotalMilliseconds > 100)
                {
                    break;
                }

                await Task.Delay(10, ct);
            }
        }

        return output.ToString();
    }

    /// <summary>
    /// Determines the appropriate compgen command based on the word context.
    /// </summary>
    private (string Command, CompletionItemType Type) DetermineCompletionCommand(WordInfo wordInfo)
    {
        // Check if completing a file path
        if (IsFilePath(wordInfo.Word))
        {
            var escapedWord = EscapeShellArgument(wordInfo.Word);
            return ($"compgen -f {escapedWord} 2>/dev/null | head -20", CompletionItemType.FilePath);
        }

        // Check if it's the first word (command completion)
        if (wordInfo.IsFirstWord)
        {
            var escapedWord = EscapeShellArgument(wordInfo.Word);
            return ($"compgen -c {escapedWord} 2>/dev/null | head -20", CompletionItemType.Command);
        }

        // For other positions, try file completion as a fallback
        var escaped = EscapeShellArgument(wordInfo.Word);
        return ($"compgen -f {escaped} 2>/dev/null | head -20", CompletionItemType.Argument);
    }

    /// <summary>
    /// Determines if a word looks like a file path.
    /// </summary>
    private bool IsFilePath(string word)
    {
        return word.StartsWith("./") ||
               word.StartsWith("../") ||
               word.StartsWith("/") ||
               word.StartsWith("~/") ||
               word.Contains("/");
    }

    /// <summary>
    /// Escapes a shell argument for safe command execution.
    /// </summary>
    private string EscapeShellArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "''";
        }

        // If already quoted, return as-is
        if ((argument.StartsWith("'") && argument.EndsWith("'")) ||
            (argument.StartsWith("\"") && argument.EndsWith("\"")))
        {
            return argument;
        }

        // Escape single quotes and wrap in single quotes
        return "'" + argument.Replace("'", "'\\''") + "'";
    }

    /// <summary>
    /// Extracts the word at the cursor position and context information.
    /// </summary>
    private WordInfo ExtractWordAtCursor(string line, int cursorPosition)
    {
        if (string.IsNullOrEmpty(line) || cursorPosition < 0 || cursorPosition > line.Length)
        {
            return new WordInfo(string.Empty, 0, 0, true);
        }

        // Find word boundaries
        int wordStart = cursorPosition;
        int wordEnd = cursorPosition;

        // Find start of word (move backwards)
        while (wordStart > 0 && !char.IsWhiteSpace(line[wordStart - 1]))
        {
            wordStart--;
        }

        // Find end of word (move forwards)
        while (wordEnd < line.Length && !char.IsWhiteSpace(line[wordEnd]))
        {
            wordEnd++;
        }

        var word = line.Substring(wordStart, wordEnd - wordStart);

        // Determine if this is the first word (command)
        var beforeWord = line.Substring(0, wordStart).Trim();
        var isFirstWord = string.IsNullOrEmpty(beforeWord);

        return new WordInfo(word, wordStart, wordEnd, isFirstWord);
    }

    /// <summary>
    /// Calculates a relevance score for history-based completions.
    /// </summary>
    private int CalculateHistoryScore(CommandHistoryEntry entry, int index)
    {
        // Base score starts at 1000 and decreases with position
        var positionScore = 1000 - (index * 10);

        // Boost score based on use count (logarithmic scale)
        var useCountBoost = (int)(Math.Log10(entry.UseCount + 1) * 100);

        // Boost score based on recency (commands used in last hour get boost)
        var recencyBoost = 0;
        var age = DateTimeOffset.UtcNow - entry.ExecutedAt;
        if (age.TotalHours < 1)
        {
            recencyBoost = 200;
        }
        else if (age.TotalDays < 1)
        {
            recencyBoost = 100;
        }
        else if (age.TotalDays < 7)
        {
            recencyBoost = 50;
        }

        return positionScore + useCountBoost + recencyBoost;
    }

    /// <summary>
    /// Calculates a relevance score for remote completions.
    /// </summary>
    private int CalculateRemoteScore(string completion, CompletionItemType type, int index)
    {
        // Base score decreases with position
        var positionScore = 500 - (index * 5);

        // Boost commands over files
        var typeBoost = type == CompletionItemType.Command ? 100 : 0;

        return positionScore + typeBoost;
    }

    /// <summary>
    /// Information about the word at cursor position.
    /// </summary>
    private record WordInfo(string Word, int Start, int End, bool IsFirstWord);
}
