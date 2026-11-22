using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace DesignatedDriver.Services;

/// <summary>What a command produced. Standard output and error are kept apart.</summary>
public sealed record CommandResult(
    string Executable,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Elapsed)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>Output for messages: standard error if there is any, otherwise standard output.</summary>
    public string Message => string.IsNullOrWhiteSpace(StandardError) ? StandardOutput.Trim() : StandardError.Trim();

    public string CommandLine => CommandLineFormat.Format(Executable, Arguments);
}

public enum ActivityKind { Command, Output, Error, Result, Note }

public sealed record ActivityEntry(ActivityKind Kind, string Text);

/// <summary>Runs git and gh. An interface so the self-test can substitute a recorder.</summary>
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default,
        string? standardInput = null);
}

/// <summary>
/// Runs a command with the repository folder as working directory, captures standard output and error
/// separately and reports every command, its output and its exit code to the activity panel.
/// </summary>
public sealed class CommandRunner : ICommandRunner
{
    public event EventHandler<ActivityEntry>? Activity;

    public void Note(string text) => Activity?.Invoke(this, new ActivityEntry(ActivityKind.Note, text));

    public async Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default,
        string? standardInput = null)
    {
        var commandLine = CommandLineFormat.Format(executable, arguments);
        Activity?.Invoke(this, new ActivityEntry(ActivityKind.Command, commandLine));

        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
        };
        foreach (var a in arguments) start.ArgumentList.Add(a);

        // Nothing here may stop and wait for a keyboard.
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GH_PROMPT_DISABLED"] = "1";
        start.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";
        start.Environment["NO_COLOR"] = "1";
        start.Environment["GH_PAGER"] = "cat";
        start.Environment["GIT_PAGER"] = "cat";
        start.Environment["PAGER"] = "cat";

        var stopwatch = Stopwatch.StartNew();
        var output = new StringBuilder();
        var error = new StringBuilder();

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (output) output.AppendLine(e.Data);
            Activity?.Invoke(this, new ActivityEntry(ActivityKind.Output, e.Data));
            onOutputLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (error) error.AppendLine(e.Data);
            Activity?.Invoke(this, new ActivityEntry(ActivityKind.Error, e.Data));
            onOutputLine?.Invoke(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            // The executable isn't installed or isn't on the path.
            stopwatch.Stop();
            var text = ex.Message;
            Activity?.Invoke(this, new ActivityEntry(ActivityKind.Error, text));
            return new CommandResult(executable, arguments, -1, "", text, stopwatch.Elapsed);
        }

        // Text for standard input (a secret, say) is written here and never appears on the command line.
        if (standardInput is not null) process.StandardInput.Write(standardInput);
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        // WaitForExitAsync can return before the last output events have been delivered.
        process.WaitForExit();
        stopwatch.Stop();

        string outText, errText;
        lock (output) outText = output.ToString();
        lock (error) errText = error.ToString();

        Activity?.Invoke(this, new ActivityEntry(ActivityKind.Result,
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "exit {0} ({1:0.0}s)", process.ExitCode, stopwatch.Elapsed.TotalSeconds)));

        return new CommandResult(executable, arguments, process.ExitCode, outText, errText, stopwatch.Elapsed);
    }
}

public static class CommandLineFormat
{
    /// <summary>Shows a command the way the user would type it, quoting arguments with spaces.</summary>
    public static string Format(string executable, IEnumerable<string> arguments)
    {
        var sb = new StringBuilder(executable);
        foreach (var a in arguments)
        {
            sb.Append(' ');
            sb.Append(a.Length == 0 || a.Any(char.IsWhiteSpace) || a.Contains('"') ? "\"" + a.Replace("\"", "\\\"") + "\"" : a);
        }
        return sb.ToString();
    }

    /// <summary>Splits a command line typed in settings into the executable and its arguments, honouring double quotes.</summary>
    public static (string Executable, List<string> Arguments) Split(string commandLine)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false, any = false;
        foreach (var c in commandLine)
        {
            if (c == '"') { inQuotes = !inQuotes; any = true; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (any) { parts.Add(current.ToString()); current.Clear(); any = false; }
                continue;
            }
            current.Append(c);
            any = true;
        }
        if (any) parts.Add(current.ToString());
        if (parts.Count == 0) return ("", []);
        return (parts[0], parts.Skip(1).ToList());
    }
}
