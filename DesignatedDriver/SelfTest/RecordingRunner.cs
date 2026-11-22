using System.IO;
using DesignatedDriver.Services;

namespace DesignatedDriver.SelfTest;

/// <summary>Stands in for git and gh during the self-test: records every call and answers with canned output.</summary>
public sealed class RecordingRunner : ICommandRunner
{
    public sealed record Call(string Executable, IReadOnlyList<string> Arguments, string? FileContent, string? StandardInput = null)
    {
        public string Line => CommandLineFormat.Format(Executable, Arguments);
        public bool Is(params string[] prefix) => Arguments.Count >= prefix.Length && prefix.Select((p, i) => Arguments[i] == p).All(x => x);
    }

    public List<Call> Calls { get; } = [];

    /// <summary>Returns a result for a call, or null for a plain success with no output.</summary>
    public Func<string, IReadOnlyList<string>, CommandResult?>? Respond { get; set; }

    public Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, string? workingDirectory, Action<string>? onOutputLine = null, CancellationToken cancellationToken = default, string? standardInput = null)
    {
        // Text passed through a file is read now, because the caller deletes the file afterwards.
        string? fileContent = null;
        for (int i = 0; i < arguments.Count - 1; i++)
        {
            if (arguments[i] is "--body-file" or "-F" && File.Exists(arguments[i + 1]))
                fileContent = File.ReadAllText(arguments[i + 1]);
        }
        Calls.Add(new Call(executable, arguments.ToList(), fileContent, standardInput));

        var result = Respond?.Invoke(executable, arguments) ?? new CommandResult(executable, arguments, 0, "", "", TimeSpan.Zero);
        return Task.FromResult(result);
    }

    public static CommandResult Ok(string executable, IReadOnlyList<string> args, string output) => new(executable, args, 0, output, "", TimeSpan.Zero);
    public static CommandResult Fail(string executable, IReadOnlyList<string> args, string error, int exitCode = 1) => new(executable, args, exitCode, "", error, TimeSpan.Zero);
}
