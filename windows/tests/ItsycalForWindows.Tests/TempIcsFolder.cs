using System;
using System.IO;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Disposable temporary folder for the <c>IcsEventSource</c> tests. The folder is created under the
/// system temp directory and removed recursively on dispose.
/// </summary>
internal sealed class TempIcsFolder : IDisposable
{
    public TempIcsFolder()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "itsycal-ics-tests",
            Guid.NewGuid().ToString("N"));

        System.IO.Directory.CreateDirectory(Path);
    }

    /// <summary>Absolute path of the temporary folder.</summary>
    public string Path { get; }

    /// <summary>Writes <paramref name="content"/> to <paramref name="fileName"/> and returns the full path.</summary>
    public string Write(string fileName, string content)
    {
        var path = System.IO.Path.Combine(Path, fileName);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Full path of a child entry without creating it.</summary>
    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Path))
            {
                System.IO.Directory.Delete(Path, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort cleanup of the test temp folder.
        }
    }
}
