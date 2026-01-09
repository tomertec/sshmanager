using System.Timers;

namespace SshManager.Terminal.Services.Playback;

/// <summary>
/// Controls playback of asciicast recordings with support for play, pause, seek, and speed control.
/// </summary>
public sealed class PlaybackController : IDisposable
{
    private readonly AsciinemaReader _reader;
    private readonly System.Timers.Timer _playbackTimer;
    private int _currentEventIndex;
    private DateTimeOffset _playbackStartTime;
    private TimeSpan _pausedPosition;
    private double _playbackSpeed = 1.0;
    private bool _isPaused = true;
    private bool _isDisposed;
    private readonly object _lock = new();

    /// <summary>
    /// Fired when terminal output should be written.
    /// </summary>
    public event Action<string>? OutputReceived;

    /// <summary>
    /// Fired when playback reaches the end.
    /// </summary>
    public event Action? PlaybackCompleted;

    /// <summary>
    /// Fired periodically with the current playback position.
    /// </summary>
    public event Action<TimeSpan>? PositionChanged;

    /// <summary>
    /// Total duration of the recording.
    /// </summary>
    public TimeSpan Duration => _reader.Duration;

    /// <summary>
    /// Current playback position.
    /// </summary>
    public TimeSpan Position { get; private set; }

    /// <summary>
    /// Playback speed multiplier (0.25x to 4.0x).
    /// </summary>
    public double Speed
    {
        get => _playbackSpeed;
        set
        {
            lock (_lock)
            {
                var newSpeed = Math.Clamp(value, 0.25, 4.0);
                if (Math.Abs(_playbackSpeed - newSpeed) < 0.001)
                    return;

                _playbackSpeed = newSpeed;

                // If playing, adjust the reference time to maintain smooth playback
                if (!_isPaused)
                {
                    var currentPosition = Position;
                    _playbackStartTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(currentPosition.TotalSeconds / _playbackSpeed);
                }
            }
        }
    }

    /// <summary>
    /// Whether playback is currently active.
    /// </summary>
    public bool IsPlaying
    {
        get
        {
            lock (_lock)
            {
                return !_isPaused && _currentEventIndex < _reader.Events.Count;
            }
        }
    }

    /// <summary>
    /// Terminal width from the recording.
    /// </summary>
    public int Width => _reader.Header.Width;

    /// <summary>
    /// Terminal height from the recording.
    /// </summary>
    public int Height => _reader.Header.Height;

    /// <summary>
    /// Creates a new playback controller for the given recording.
    /// </summary>
    /// <param name="reader">The loaded asciicast recording.</param>
    /// <exception cref="ArgumentNullException">Thrown when reader is null.</exception>
    public PlaybackController(AsciinemaReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _playbackTimer = new System.Timers.Timer(16); // ~60 FPS
        _playbackTimer.Elapsed += OnTimerTick;
        _playbackTimer.AutoReset = true;
    }

    /// <summary>
    /// Starts or resumes playback.
    /// </summary>
    public void Play()
    {
        lock (_lock)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(PlaybackController));

            if (!_isPaused)
                return;

            _isPaused = false;

            // Set start time accounting for paused position and speed
            _playbackStartTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(_pausedPosition.TotalSeconds / _playbackSpeed);

            _playbackTimer.Start();
        }
    }

    /// <summary>
    /// Pauses playback at the current position.
    /// </summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(PlaybackController));

            if (_isPaused)
                return;

            _isPaused = true;
            _pausedPosition = Position;
            _playbackTimer.Stop();
        }
    }

    /// <summary>
    /// Stops playback and resets to the beginning.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(PlaybackController));

            var wasPlaying = !_isPaused;

            _isPaused = true;
            _playbackTimer.Stop();
            _currentEventIndex = 0;
            _pausedPosition = TimeSpan.Zero;
            Position = TimeSpan.Zero;

            if (wasPlaying)
            {
                PositionChanged?.Invoke(Position);
            }
        }
    }

    /// <summary>
    /// Seeks to a specific position in the recording.
    /// </summary>
    /// <param name="position">Target position.</param>
    public void Seek(TimeSpan position)
    {
        lock (_lock)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(PlaybackController));

            var targetSeconds = Math.Clamp(position.TotalSeconds, 0, Duration.TotalSeconds);
            var wasPlaying = !_isPaused;

            // Find the event index for this timestamp
            var newIndex = _reader.GetEventIndexAtTime(targetSeconds);
            if (newIndex < 0)
                newIndex = _reader.Events.Count;

            _currentEventIndex = newIndex;
            _pausedPosition = TimeSpan.FromSeconds(targetSeconds);
            Position = _pausedPosition;

            // Replay all output events up to this point to restore terminal state
            ReplayToCurrentPosition();

            // Adjust start time if playing
            if (wasPlaying)
            {
                _playbackStartTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(targetSeconds / _playbackSpeed);
            }

            PositionChanged?.Invoke(Position);
        }
    }

    /// <summary>
    /// Replays all output events from the beginning to the current position.
    /// This restores the terminal state when seeking.
    /// </summary>
    private void ReplayToCurrentPosition()
    {
        var targetTime = Position.TotalSeconds;

        for (var i = 0; i < _currentEventIndex && i < _reader.Events.Count; i++)
        {
            var evt = _reader.Events[i];
            if (evt.Timestamp <= targetTime && evt.EventType == "o")
            {
                OutputReceived?.Invoke(evt.Data);
            }
        }
    }

    /// <summary>
    /// Timer callback that processes events at the current playback position.
    /// </summary>
    private void OnTimerTick(object? sender, ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (_isPaused || _isDisposed)
                return;

            // Calculate current position based on elapsed time and speed
            var elapsed = DateTimeOffset.UtcNow - _playbackStartTime;
            var currentSeconds = elapsed.TotalSeconds * _playbackSpeed;
            Position = TimeSpan.FromSeconds(currentSeconds);

            // Process all events up to current time
            while (_currentEventIndex < _reader.Events.Count)
            {
                var evt = _reader.Events[_currentEventIndex];

                if (evt.Timestamp > currentSeconds)
                    break;

                // Only emit output events (type "o"), ignore input events (type "i") during playback
                if (evt.EventType == "o")
                {
                    OutputReceived?.Invoke(evt.Data);
                }

                _currentEventIndex++;
            }

            // Notify position change
            PositionChanged?.Invoke(Position);

            // Check if playback completed
            if (_currentEventIndex >= _reader.Events.Count)
            {
                _isPaused = true;
                _playbackTimer.Stop();
                PlaybackCompleted?.Invoke();
            }
        }
    }

    /// <summary>
    /// Disposes the playback controller and releases resources.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _playbackTimer.Stop();
            _playbackTimer.Dispose();
        }
    }
}
