using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

public sealed record ExternalToolResult(int ExitCode, string StandardOutput, string StandardError);

public static class ExternalToolRunner
{
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
        string? standardInput)
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

        process.WaitForExit();
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
