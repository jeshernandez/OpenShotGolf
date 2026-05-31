using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

public sealed record ExternalToolResult(int ExitCode, string StandardOutput, string StandardError);

public static class ExternalToolRunner
{
    // Generous upper bound: real GDAL/PDAL passes over large rasters can take minutes, but a
    // process that blocks indefinitely (e.g. waiting on stdin that never closes) must not
    // freeze the editor. Exceeding this kills the process and surfaces a TimeoutException.
    private const int DefaultTimeoutMs = 600_000;

    public static void EnsureCommandAvailable(string command, string label)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new InvalidOperationException($"{label} command is empty.");
        }

        if (LooksLikeFilePath(command) && !File.Exists(command))
        {
            throw new FileNotFoundException($"{label} command was not found: {command}");
        }

        try
        {
            Run(command, ["--version"]);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"{label} command is not available: {command}. Use the executable name if it is on PATH, or set the full path.", exception);
        }
    }

    public static ExternalToolResult Run(string command, IEnumerable<string> arguments, string? workingDirectory = null)
    {
        return Run(command, arguments, workingDirectory, null);
    }

    public static ExternalToolResult RunWithInput(
        string command,
        IEnumerable<string> arguments,
        string standardInput,
        string? workingDirectory = null)
    {
        return Run(command, arguments, workingDirectory, standardInput);
    }

    private static ExternalToolResult Run(
        string command,
        IEnumerable<string> arguments,
        string? workingDirectory,
        string? standardInput,
        int timeoutMs = DefaultTimeoutMs)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput != null,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data != null)
            {
                output.AppendLine(eventArgs.Data);
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data != null)
            {
                error.AppendLine(eventArgs.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Could not start {command}.");
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Could not start {command}: {exception.Message}", exception);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (standardInput != null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // Best effort: the process may have exited between the timeout and the kill.
            }

            throw new TimeoutException(
                $"{command} did not complete within {timeoutMs / 1000} seconds and was terminated.");
        }

        // Second call (no timeout) flushes pending async output events after process exit.
        process.WaitForExit();

        var standardOutput = output.ToString().Trim();
        var standardError = error.ToString().Trim();
        if (process.ExitCode != 0)
        {
            var message = new StringBuilder();
            message.AppendLine($"{command} failed with exit code {process.ExitCode}.");
            if (!string.IsNullOrWhiteSpace(standardError))
            {
                message.AppendLine(standardError);
            }

            if (!string.IsNullOrWhiteSpace(standardOutput))
            {
                message.AppendLine(standardOutput);
            }

            throw new InvalidOperationException(message.ToString().TrimEnd());
        }

        return new ExternalToolResult(process.ExitCode, standardOutput, standardError);
    }

    private static bool LooksLikeFilePath(string command)
    {
        return command.Contains(Path.DirectorySeparatorChar)
            || command.Contains(Path.AltDirectorySeparatorChar);
    }
}
