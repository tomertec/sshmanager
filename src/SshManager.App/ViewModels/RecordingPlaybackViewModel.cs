using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.Core.Models;
using SshManager.Terminal.Controls;
using SshManager.Terminal.Services;
using SshManager.Terminal.Services.Recording;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for playing back recorded terminal sessions.
/// </summary>
public partial class RecordingPlaybackViewModel : ObservableObject
{
    private readonly ISessionRecordingService _recordingService;
    private readonly SessionRecording _recording;
    private WebTerminalControl? _terminalControl;
    private CancellationTokenSource? _playbackCts;
    private DispatcherTimer? _timer;
    private List<RecordingFrame>? _frames;
    private int _currentFrameIndex;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private TimeSpan _position = TimeSpan.Zero;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private double _selectedSpeed = 1.0;

    [ObservableProperty]
    private string _recordingTitle = string.Empty;

    public double PositionSeconds
    {
        get => Position.TotalSeconds;
        set
        {
            if (Math.Abs(value - Position.TotalSeconds) > 0.1)
            {
                SeekTo(TimeSpan.FromSeconds(value));
            }
        }
    }

    public string PlayPauseButtonText => IsPlaying ? "Pause" : "Play";

    public List<double> SpeedOptions { get; } = new() { 0.25, 0.5, 1.0, 1.5, 2.0, 4.0 };

    public RecordingPlaybackViewModel(
        ISessionRecordingService recordingService,
        SessionRecording recording)
    {
        _recordingService = recordingService;
        _recording = recording;
        Duration = recording.Duration;
        RecordingTitle = $"{recording.Title} - {recording.StartedAt:yyyy-MM-dd HH:mm}";
    }

    /// <summary>
    /// Sets the terminal control reference for playback.
    /// </summary>
    public void SetTerminalControl(WebTerminalControl terminalControl)
    {
        _terminalControl = terminalControl;
    }

    /// <summary>
    /// Initializes the playback session.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_terminalControl == null)
            return;

        try
        {
            // Initialize the WebTerminalControl first (required before any operations)
            await _terminalControl.InitializeAsync();

            // Load recording frames
            _frames = await _recordingService.LoadRecordingAsync(_recording.Id);

            if (_frames == null || _frames.Count == 0)
            {
                throw new InvalidOperationException("Recording has no frames");
            }

            // Clear terminal
            _terminalControl.Clear();

            // Set up timer for position updates
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _timer.Tick += Timer_Tick;
        }
        catch (Exception ex)
        {
            // Log error
            System.Diagnostics.Debug.WriteLine($"Failed to initialize playback: {ex.Message}");
        }
    }

    /// <summary>
    /// Cleans up resources when closing.
    /// </summary>
    public async Task CleanupAsync()
    {
        await StopAsync();
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>
    /// Toggles playback between play and pause.
    /// </summary>
    [RelayCommand]
    private async Task PlayPauseAsync()
    {
        if (IsPlaying)
        {
            await PauseAsync();
        }
        else
        {
            await PlayAsync();
        }
    }

    /// <summary>
    /// Stops playback and resets to the beginning.
    /// </summary>
    [RelayCommand]
    private async Task StopAsync()
    {
        await PauseAsync();
        SeekTo(TimeSpan.Zero);
    }

    partial void OnSelectedSpeedChanged(double value)
    {
        // If playing, restart playback with new speed
        if (IsPlaying)
        {
            var currentPosition = Position;
            _ = Task.Run(async () =>
            {
                await PauseAsync();
                await PlayAsync();
            });
        }
    }

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayPauseButtonText));
    }

    private async Task PlayAsync()
    {
        if (_frames == null || _frames.Count == 0 || _terminalControl == null)
            return;

        IsPlaying = true;
        _playbackCts = new CancellationTokenSource();
        _timer?.Start();

        var token = _playbackCts.Token;
        var dispatcher = System.Windows.Application.Current.Dispatcher;

        try
        {
            // Run playback on background thread to avoid blocking UI
            await Task.Run(async () =>
            {
                var startTime = DateTime.UtcNow;
                var startPosition = Position;

                while (_currentFrameIndex < _frames.Count && !token.IsCancellationRequested)
                {
                    var frame = _frames[_currentFrameIndex];
                    var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds * SelectedSpeed;
                    var targetTime = startPosition + TimeSpan.FromMilliseconds(elapsed);

                    if (frame.Timestamp <= targetTime)
                    {
                        // Write frame to terminal on UI thread
                        dispatcher.Invoke(() =>
                        {
                            if (!token.IsCancellationRequested && _terminalControl != null)
                            {
                                _terminalControl.WriteData(frame.Data);
                                Position = frame.Timestamp;
                            }
                        });
                        _currentFrameIndex++;
                    }
                    else
                    {
                        // Wait for next frame time
                        var delay = (int)((frame.Timestamp - targetTime).TotalMilliseconds / SelectedSpeed);
                        if (delay > 0)
                        {
                            await Task.Delay(Math.Min(delay, 50), token);
                        }
                        else
                        {
                            // Yield to prevent tight loop
                            await Task.Delay(1, token);
                        }
                    }
                }
            }, token);

            // Reached end of recording
            if (_currentFrameIndex >= _frames.Count)
            {
                IsPlaying = false;
                _timer?.Stop();
            }
        }
        catch (OperationCanceledException)
        {
            // Playback was paused/stopped
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Playback error: {ex.Message}");
            IsPlaying = false;
            _timer?.Stop();
        }
    }

    private async Task PauseAsync()
    {
        IsPlaying = false;
        _playbackCts?.Cancel();
        _timer?.Stop();
        await Task.Delay(50); // Give time for playback to stop
    }

    private void SeekTo(TimeSpan targetPosition)
    {
        if (_frames == null || _frames.Count == 0 || _terminalControl == null)
            return;

        // Run seek operation asynchronously but don't block the property setter
        _ = SeekToAsync(targetPosition);
    }

    private async Task SeekToAsync(TimeSpan targetPosition)
    {
        if (_frames == null || _frames.Count == 0 || _terminalControl == null)
            return;

        try
        {
            var wasPlaying = IsPlaying;
            if (wasPlaying)
            {
                await PauseAsync();
            }

            // Find frame index for target position
            _currentFrameIndex = _frames.FindIndex(f => f.Timestamp >= targetPosition);
            if (_currentFrameIndex == -1)
            {
                _currentFrameIndex = _frames.Count - 1;
            }

            // Replay all frames up to target position
            _terminalControl.Clear();

            for (int i = 0; i <= _currentFrameIndex && i < _frames.Count; i++)
            {
                _terminalControl.WriteData(_frames[i].Data);
            }

            Position = targetPosition;

            if (wasPlaying)
            {
                await PlayAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Seek error: {ex.Message}");
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        // Timer updates position in UI
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(PositionSeconds));
    }
}
