using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Models;

namespace ItsycalForWindows.Services;

/// <summary>
/// Event source backed by the <c>*.ics</c> files in a local folder. This is the first offline
/// calendar provider: friends, family and tooling can drop standard iCalendar exports into the
/// folder and they appear next to the Windows appointment store.
/// </summary>
/// <remarks>
/// <para>
/// The default folder is <c>%LOCALAPPDATA%\ItsycalForWindows\calendars</c>; it is created lazily on
/// the first read and a missing folder simply yields no events. Parsing is delegated to
/// <see cref="IcsParser"/>; this class only handles the folder, the file cache, range filtering and
/// ordering.
/// </para>
/// <para>
/// Parsed text is cached per file and invalidated by the file's last-write time and length, so a
/// month-to-month read does not re-read unchanged files while an edited export is picked up on the
/// next refresh. Any I/O or parse failure is swallowed and contributes no events, as required by
/// the <see cref="IEventSource"/> contract.
/// </para>
/// </remarks>
public sealed class IcsEventSource : IEventSource
{
    /// <summary>Folder name under <c>%LOCALAPPDATA%</c> used by the default instance.</summary>
    public const string FolderName = "calendars";

    /// <summary>Files bigger than this are ignored (a runaway export must not stall the flyout).</summary>
    public const long MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>Maximum number of .ics files read per refresh, in deterministic name order.</summary>
    public const int MaxFiles = 64;

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, CachedFile> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _directory;

    private bool _directoryEnsured;

    /// <summary>Creates a source reading the default folder.</summary>
    public IcsEventSource()
        : this(DefaultDirectory)
    {
    }

    /// <summary>Creates a source reading <paramref name="directory"/>.</summary>
    /// <param name="directory">Folder that contains the <c>*.ics</c> files.</param>
    public IcsEventSource(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <summary>The folder this source reads.</summary>
    public string Directory => _directory;

    /// <summary>The default folder: <c>%LOCALAPPDATA%\ItsycalForWindows\calendars</c>.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ItsycalForWindows",
        FolderName);

    /// <inheritdoc />
    public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default)
    {
        if (toExclusive <= fromInclusive || cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<IReadOnlyList<CalendarEvent>>(Array.Empty<CalendarEvent>());
        }

#pragma warning disable CA1031 // IEventSource requires that folder failures never reach the UI.
        try
        {
            EnsureDirectory();
            return Task.FromResult(ReadFolder(fromInclusive, toExclusive, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult<IReadOnlyList<CalendarEvent>>(Array.Empty<CalendarEvent>());
        }
        catch (Exception)
        {
            return Task.FromResult<IReadOnlyList<CalendarEvent>>(Array.Empty<CalendarEvent>());
        }
#pragma warning restore CA1031
    }

    private IReadOnlyList<CalendarEvent> ReadFolder(
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken)
    {
        if (!System.IO.Directory.Exists(_directory))
        {
            return Array.Empty<CalendarEvent>();
        }

        var paths = System.IO.Directory
            .EnumerateFiles(_directory, "*.ics", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxFiles)
            .ToArray();

        var events = new List<CalendarEvent>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = TryGetFileText(path);
            if (text is null)
            {
                continue;
            }

            events.AddRange(IcsParser.Parse(text, fromInclusive, toExclusive));
        }

        PruneCache(paths);

        events.Sort(static (left, right) =>
        {
            var byStart = left.Start.CompareTo(right.Start);
            if (byStart != 0)
            {
                return byStart;
            }

            var bySubject = string.Compare(left.Subject, right.Subject, StringComparison.OrdinalIgnoreCase);
            return bySubject != 0 ? bySubject : left.End.CompareTo(right.End);
        });

        return events;
    }

    private string? TryGetFileText(string path)
    {
#pragma warning disable CA1031 // A single unreadable file must not fail the whole refresh.
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes)
            {
                return null;
            }

            var length = info.Length;
            var lastWriteUtcTicks = info.LastWriteTimeUtc.Ticks;

            lock (_cacheLock)
            {
                if (_cache.TryGetValue(path, out var cached) &&
                    cached.Length == length &&
                    cached.LastWriteUtcTicks == lastWriteUtcTicks)
                {
                    return cached.Text;
                }
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception)
            {
                return null;
            }

            lock (_cacheLock)
            {
                _cache[path] = new CachedFile(length, lastWriteUtcTicks, text);
            }

            return text;
        }
        catch (Exception)
        {
            return null;
        }
#pragma warning restore CA1031
    }

    private void PruneCache(IReadOnlyCollection<string> currentPaths)
    {
        var current = new HashSet<string>(currentPaths, StringComparer.OrdinalIgnoreCase);

        lock (_cacheLock)
        {
            foreach (var cachedPath in _cache.Keys.Where(path => !current.Contains(path)).ToArray())
            {
                _cache.Remove(cachedPath);
            }
        }
    }

    private void EnsureDirectory()
    {
        if (_directoryEnsured)
        {
            return;
        }

#pragma warning disable CA1031 // A read-only profile still has to degrade to "no events".
        try
        {
            System.IO.Directory.CreateDirectory(_directory);
            _directoryEnsured = true;
        }
        catch (Exception)
        {
            // Retried on the next refresh; reads return no events in the meantime.
        }
#pragma warning restore CA1031
    }

    private sealed record CachedFile(long Length, long LastWriteUtcTicks, string Text);
}
