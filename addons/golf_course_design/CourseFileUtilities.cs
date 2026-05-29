using System;
using System.IO;
using System.Linq;
using Godot;

public static class CourseFileUtilities
{
    public static string NormalizeProjectPath(string path)
    {
        var trimmed = path.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return trimmed.Replace('\\', '/');
    }

    public static string ToAbsolutePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return Path.IsPathRooted(normalized)
            ? normalized
            : ProjectSettings.GlobalizePath(normalized);
    }

    public static bool ArePathsSame(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    public static void EnsureCleanDirectory(string absolutePath)
    {
        if (Directory.Exists(absolutePath))
        {
            Directory.Delete(absolutePath, true);
        }

        Directory.CreateDirectory(absolutePath);
    }

    public static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(sourceDirectory);
        }

        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? destinationDirectory);
            File.Copy(file, destination, true);
        }
    }

    public static bool IsDirectoryEmpty(string absolutePath)
    {
        return !Directory.EnumerateFileSystemEntries(absolutePath).Any();
    }
}
