using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace JobOrchestrator.BuildingBlocks;

/// <summary>One event of the trail: what happened, in which part of the app, how long it took and the error code when it failed.</summary>
public sealed record TrailEntry(long Id, DateTime At, string Kind, string Level, string Name, string? Detail, int? Ms, string? Code, string? Trace, IReadOnlyDictionary<string, string?>? Data);

/// <summary>
/// Rastro: every request that changes something or fails, every command and its result, each step of the queue, each change of state,
/// the AI calls, the warnings and errors of every module and what happens on the screen (clicks, pages, errors), kept on this machine
/// to hunt bugs. One JSON line per event in data/logs/trail-YYYY-MM-DD.jsonl, 14 days kept, the last 5,000 also in memory for the
/// Diagnóstico screen. Request and form bodies never go in: no answer, no CV, no e-mail. Nothing leaves the computer.
/// </summary>
public sealed class Trail : IAsyncDisposable
{
    /// <summary>The trail of this process, for the places with no dependency injection (the domain, the logger provider).</summary>
    public static Trail? Shared { get; private set; }

    private static readonly AsyncLocal<string?> CurrentTrace = new();
    /// <summary>What ties the events of one request or one step together (the request id, or "app:{id}" for a queue step).</summary>
    public static string? Trace { get => CurrentTrace.Value; set => CurrentTrace.Value = value; }

    private const int Keep = 5000;
    private readonly TrailEntry?[] ring = new TrailEntry?[Keep];
    private readonly Lock gate = new();
    private readonly Channel<TrailEntry> pending = Channel.CreateBounded<TrailEntry>(new BoundedChannelOptions(50_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly TimeProvider time;
    private readonly string folder;
    private readonly Task writer;
    private long next;

    public Trail(TimeProvider time, string? folder = null)
    {
        this.time = time;
        this.folder = folder ?? AppPaths.In(AppPaths.Data, "logs");
        writer = Task.Run(WriteAsync);
        Shared = this;
    }

    public string Folder => folder;

    public void Add(string kind, string level, string name, string? detail = null, int? ms = null, string? code = null, IReadOnlyDictionary<string, string?>? data = null, string? trace = null)
    {
        var entry = new TrailEntry(Interlocked.Increment(ref next), time.GetUtcNow().UtcDateTime, kind, level, Cut(name, 200)!, Cut(detail, 2000), ms, Cut(code, 80), trace ?? Trace, data);
        lock (gate) ring[(int)(entry.Id % Keep)] = entry;
        pending.Writer.TryWrite(entry);
    }

    public void Info(string kind, string name, string? detail = null, int? ms = null, IReadOnlyDictionary<string, string?>? data = null) => Add(kind, "info", name, detail, ms, null, data);
    public void Warn(string kind, string name, string? detail = null, int? ms = null, string? code = null, IReadOnlyDictionary<string, string?>? data = null) => Add(kind, "warn", name, detail, ms, code, data);
    public void Error(string kind, string name, string? detail = null, int? ms = null, string? code = null, IReadOnlyDictionary<string, string?>? data = null) => Add(kind, "error", name, detail, ms, code, data);

    /// <summary>The events in memory, newest first, filtered by kind, level ("warn" also brings errors), text and trace.</summary>
    public IReadOnlyList<TrailEntry> Recent(string? kind = null, string? level = null, string? text = null, string? trace = null, long after = 0, int limit = 300)
    {
        TrailEntry[] all;
        lock (gate) all = [.. ring.OfType<TrailEntry>()];
        var kinds = string.IsNullOrWhiteSpace(kind) ? null : kind.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. all.Where(e => e.Id > after)
            .Where(e => kinds is null || kinds.Contains(e.Kind))
            .Where(e => level switch { "error" => e.Level == "error", "warn" => e.Level is "warn" or "error", _ => true })
            .Where(e => string.IsNullOrWhiteSpace(trace) || e.Trace == trace)
            .Where(e => string.IsNullOrWhiteSpace(text) || $"{e.Name} {e.Detail} {e.Code} {string.Join(' ', e.Data?.Values ?? [])}".Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Id).Take(Math.Clamp(limit, 1, Keep))];
    }

    /// <summary>The days kept on disk, newest first.</summary>
    public IReadOnlyList<(string Day, long Bytes)> Days() =>
        Directory.Exists(folder) ? [.. new DirectoryInfo(folder).GetFiles("trail-*.jsonl").OrderByDescending(f => f.Name).Select(f => (f.Name[6..^6], f.Length))] : [];

    public string? FileOf(string day) =>
        day.Length == 10 && day.All(c => char.IsDigit(c) || c == '-') && Path.Combine(folder, $"trail-{day}.jsonl") is var path && File.Exists(path) ? path : null;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private async Task WriteAsync()
    {
        var day = "";
        StreamWriter? file = null;
        try
        {
            while (await pending.Reader.WaitToReadAsync())
            {
                while (pending.Reader.TryRead(out var e))
                {
                    var today = e.At.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    if (today != day || file is null)
                    {
                        if (file is not null) await file.DisposeAsync();
                        Directory.CreateDirectory(folder);
                        file = new StreamWriter(new FileStream(Path.Combine(folder, $"trail-{today}.jsonl"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
                        day = today;
                        Prune(e.At);
                    }
                    await file.WriteLineAsync(JsonSerializer.Serialize(e, Json));
                }
                if (file is not null) await file.FlushAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a full or read-only disk cannot take the app down */ }
        finally { if (file is not null) await file.DisposeAsync(); }
    }

    private void Prune(DateTime now)
    {
        try
        {
            foreach (var f in new DirectoryInfo(folder).GetFiles("trail-*.jsonl"))
                if (DateTime.TryParse(f.Name[6..^6], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) && d < now.Date.AddDays(-14)) f.Delete();
        }
        catch (IOException) { }
    }

    private static string? Cut(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max] + "…";

    public async ValueTask DisposeAsync()
    {
        pending.Writer.TryComplete();
        try { await writer.WaitAsync(TimeSpan.FromSeconds(3)); } catch (TimeoutException) { }
    }
}

/// <summary>Every warning and error any module logs, and the app's own information lines (AI calls, searches, the queue), go to the trail too.</summary>
public sealed class TrailLoggerProvider(Trail trail) : ILoggerProvider
{
    public ILogger CreateLogger(string category) => new TrailLogger(trail, category);
    public void Dispose() { }

    private sealed class TrailLogger(Trail trail, string category) : ILogger
    {
        private readonly bool own = category.StartsWith("JobOrchestrator.", StringComparison.Ordinal);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning || own && level == LogLevel.Information;

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            // EF warnings about query shape are noise; the AI calls have their own events.
            if (category.StartsWith("Microsoft.EntityFrameworkCore.Query", StringComparison.Ordinal)) return;
            if (level == LogLevel.Information && category.EndsWith(".IntelligenceApi", StringComparison.Ordinal)) return;
            var message = formatter(state, exception);
            // A new database has no migration history yet: EF reports the first read of it as an error and then creates the table.
            if (category == "Microsoft.EntityFrameworkCore.Database.Command" && message.Contains("__EFMigrationsHistory", StringComparison.Ordinal)) return;
            var detail = exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}";
            var name = category.Length > 60 ? category[(category.LastIndexOf('.', 50) + 1)..] : category;
            trail.Add("log", level >= LogLevel.Error ? "error" : level == LogLevel.Warning ? "warn" : "info", name, detail,
                data: exception?.StackTrace is { } stack ? new Dictionary<string, string?> { ["stack"] = stack.Length > 1500 ? stack[..1500] : stack } : null);
        }
    }
}
