using System;
using System.IO;

namespace ItsycalForWindows.Tests;

/// <summary>Disposable temp directory for provider store and ICS subscription tests.</summary>
internal sealed class ProviderTestRoot : IDisposable
{
    public ProviderTestRoot()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "itsycal-provider-tests",
            Guid.NewGuid().ToString("N"));

        System.IO.Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string Write(string fileName, string content)
    {
        var path = Combine(fileName);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

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
