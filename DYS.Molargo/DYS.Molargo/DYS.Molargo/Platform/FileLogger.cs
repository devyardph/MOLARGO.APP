using System.Text;
using Microsoft.Extensions.Logging;

namespace DYS.Molargo.Platform;

/// <summary>
/// Writes the app's log to a file beside its cache, in debug builds.
/// </summary>
/// <remarks>
/// <para>
/// Because <c>AddDebug()</c> writes to the attached debugger's output window, and an app
/// started any other way therefore writes its exceptions nowhere. Blazor catches an
/// unhandled exception, shows the "Something went wrong" bar and logs the detail — so the
/// one thing needed to diagnose it was the one thing being discarded.
/// </para>
/// <para>
/// Debug builds only, and the registration is inside the same <c>#if DEBUG</c> as the
/// developer tools. A release build writing every log line to a file on a tablet would be
/// patient data accumulating on disk with nothing ever clearing it.
/// </para>
/// </remarks>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly Lock _gate = new();

    public FileLoggerProvider(string path)
    {
        _path = path;

        // Truncated per run, so what is in it is this run. An appended file from six
        // launches ago is how a fixed bug gets reported twice.
        try
        {
            File.WriteAllText(
                _path, $"Molargo log — {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // A log that cannot be written must not stop the app starting.
        }
    }

    /// <summary>Where the log is, so it can be printed on screen rather than hunted for.</summary>
    public string Path => _path;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        // Locked and opened per line. Slow, and deliberately: this runs in debug builds to
        // catch a crash, and a buffered writer loses exactly the last few lines — which are
        // the ones that matter.
        lock (_gate)
        {
            try
            {
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _owner;
        private readonly string _category;

        public FileLogger(FileLoggerProvider owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var text = new StringBuilder()
                .Append(DateTime.Now.ToString("HH:mm:ss.fff"))
                .Append("  ")
                .Append(logLevel)
                .Append("  ")
                .Append(_category)
                .Append(": ")
                .Append(formatter(state, exception));

            // The whole chain, not just the outer message. An "Unable to resolve service"
            // names the type it could not resolve in the inner exception, and the outer one
            // says only that something failed to activate.
            for (var inner = exception; inner is not null; inner = inner.InnerException)
            {
                text.AppendLine()
                    .Append("    ")
                    .Append(inner.GetType().FullName)
                    .Append(": ")
                    .Append(inner.Message);

                if (inner.StackTrace is { Length: > 0 } stack)
                {
                    text.AppendLine().Append(stack);
                }
            }

            _owner.Write(text.ToString());
        }
    }
}
