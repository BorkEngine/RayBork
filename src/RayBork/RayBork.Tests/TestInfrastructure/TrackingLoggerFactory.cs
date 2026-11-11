using Microsoft.Extensions.Logging;

namespace RayBork.Tests;

/// <summary>
/// A caller-owned logger factory that records created categories, log entries, and dispose calls.
/// It filters at <c>minimumLevel</c>, so tests can also observe the app's default-level behavior.
/// </summary>
internal sealed class TrackingLoggerFactory : ILoggerFactory {
    private readonly Lock _gate = new();
    private readonly LogLevel _minimumLevel;
    private readonly List<TrackedLogEntry> _entries = [];
    private readonly List<string> _categories = [];
    private int _disposeCount;
    private bool _disposed;

    internal TrackingLoggerFactory(LogLevel minimumLevel = LogLevel.Trace) {
        _minimumLevel = minimumLevel;
    }

    public IReadOnlyList<TrackedLogEntry> Entries {
        get {
            lock (_gate) {
                return [.. _entries];
            }
        }
    }

    public IReadOnlyList<string> Categories {
        get {
            lock (_gate) {
                return [.. _categories];
            }
        }
    }

    public int DisposeCount {
        get {
            lock (_gate) {
                return _disposeCount;
            }
        }
    }

    public bool IsDisposed {
        get {
            lock (_gate) {
                return _disposed;
            }
        }
    }

    public ILogger CreateLogger(string categoryName) {
        ArgumentNullException.ThrowIfNull(categoryName);
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_categories.Contains(categoryName)) {
                _categories.Add(categoryName);
            }
        }

        return new TrackingLogger(this, categoryName);
    }

    public void AddProvider(ILoggerProvider provider) {
        ArgumentNullException.ThrowIfNull(provider);
    }

    public void Dispose() {
        lock (_gate) {
            _disposed = true;
            _disposeCount++;
        }
    }

    internal bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _minimumLevel;

    internal void Add(string categoryName, LogLevel level, string message, Exception? exception) {
        lock (_gate) {
            _entries.Add(new TrackedLogEntry(categoryName, level, message, exception));
        }
    }

    private sealed class TrackingLogger : ILogger {
        private readonly TrackingLoggerFactory _factory;
        private readonly string _categoryName;

        internal TrackingLogger(TrackingLoggerFactory factory, string categoryName) {
            _factory = factory;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _factory.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel)) {
                return;
            }

            _factory.Add(_categoryName, logLevel, formatter(state, exception), exception);
        }
    }
}

/// <summary>
/// One entry recorded by <see cref="TrackingLoggerFactory"/>.
/// </summary>
internal readonly record struct TrackedLogEntry(string Category, LogLevel Level, string Message, Exception? Exception);
