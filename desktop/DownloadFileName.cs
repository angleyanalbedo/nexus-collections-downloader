using System.IO;

namespace Ash.Nexus;

internal static class DownloadFileName
{
    public static bool HasArchiveExtension(string name) =>
        new[] { ".zip", ".rar", ".7z", ".pak", ".tar", ".gz", ".exe", ".bin" }
            .Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    public static string Resolve(string? dispositionName, string fallback)
    {
        // Content-Disposition is authoritative; a version suffix such as .3 is not a file format.
        var name = string.IsNullOrWhiteSpace(dispositionName)
            ? fallback
            : Path.GetFileName(dispositionName.Trim().Trim('"').Replace('/', '\\'));
        if (string.IsNullOrWhiteSpace(name)) name = "download.bin";
        return Path.HasExtension(name) ? name : name + ".bin";
    }
}
