using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DesignatedDriver.Core;
using DesignatedDriver.Resources;
using DesignatedDriver.Services;

namespace DesignatedDriver;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --self-test: run the built-in checks, print PASS or FAIL per check, and exit with the number of failures.
        if (e.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            AttachToConsole();
            int failures = SelfTest.SelfTestRunner.Run(Console.Out);
            Console.Out.Flush();
            Shutdown(failures);
            return;
        }

        // --scan <folder or owner/name>: print the scan report and exit 0 (already set up), 1 (convertible) or 2 (not convertible).
        int scanAt = Array.FindIndex(e.Args, a => a.Equals("--scan", StringComparison.OrdinalIgnoreCase));
        if (scanAt >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            AttachToConsole();
            // On the thread pool: awaiting on the dispatcher thread while blocking it would never come back.
            var target = scanAt + 1 < e.Args.Length ? e.Args[scanAt + 1] : "";
            int code = Task.Run(() => ScanFromCommandLine(target)).GetAwaiter().GetResult();
            Console.Out.Flush();
            Shutdown(code);
            return;
        }

        // --setup <answers.json>: run the set-up plan from a terminal, printing every step and command.
        // Exit 0 done, 1 a step failed, 2 the scan blocks it, 3 bad arguments.
        int setupAt = Array.FindIndex(e.Args, a => a.Equals("--setup", StringComparison.OrdinalIgnoreCase));
        if (setupAt >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            AttachToConsole();
            var answersFile = setupAt + 1 < e.Args.Length ? e.Args[setupAt + 1] : "";
            int code = Task.Run(() => SetupFromCommandLine(answersFile)).GetAwaiter().GetResult();
            Console.Out.Flush();
            Shutdown(code);
            return;
        }

        // --check <folder> [--apply [--all]]: print what has drifted from the convention and, with --apply, put the
        // ticked items right (--all ticks the tailored files too). Exit 0 in step or applied, 1 drift or a failed step, 2 not set up, 3 bad arguments.
        int checkAt = Array.FindIndex(e.Args, a => a.Equals("--check", StringComparison.OrdinalIgnoreCase));
        if (checkAt >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            AttachToConsole();
            var folder = checkAt + 1 < e.Args.Length ? e.Args[checkAt + 1] : "";
            bool apply = e.Args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
            bool all = e.Args.Contains("--all", StringComparer.OrdinalIgnoreCase);
            int code = Task.Run(() => CheckFromCommandLine(folder, apply, all)).GetAwaiter().GetResult();
            Console.Out.Flush();
            Shutdown(code);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var workspace = new Workspace(Settings.Load());
        var window = new MainWindow(workspace);
        MainWindow = window;
        window.Show();
    }

    /// <summary>The same scan the Scan screen runs, with every command echoed, for checking the tool from a terminal.</summary>
    private static async Task<int> ScanFromCommandLine(string target)
    {
        if (target.Length == 0)
        {
            Console.Error.WriteLine(Strings.Scan_NoTarget);
            return 3;
        }
        var workspace = new Workspace(new Settings());
        workspace.Runner.Activity += (_, entry) => { if (entry.Kind == ActivityKind.Command) Console.Out.WriteLine("> " + entry.Text); };

        bool isFolder = Directory.Exists(target);
        var facts = await new ScanService(workspace).ScanAsync(isFolder ? target : null, isFolder ? "" : target, CancellationToken.None);
        var report = ScanRules.Assess(facts);
        Console.Out.WriteLine();
        Console.Out.Write(ScanReportText.Render(facts, report, DateTime.Now));
        return report.Verdict switch { ScanVerdict.AlreadySetUp => 0, ScanVerdict.Convertible => 1, _ => 2 };
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(string.Format(Strings.Error_Unexpected, e.Exception.Message), Strings.App_Name, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    // A WPF application has no console of its own. For --self-test the PASS and FAIL lines go to the standard
    // output it inherited when there is one (dotnet run, a pipe, a redirect to a file); otherwise it attaches to
    // the console it was started from, and opens one if there isn't any. Attaching first would replace an
    // inherited pipe with the console, and the output would never reach the caller.
    private static void AttachToConsole()
    {
        if (!HasUsableStandardOutput() && !AttachConsole(-1)) AllocConsole();
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
        Console.SetOut(stdout);
        Console.SetError(new StreamWriter(Console.OpenStandardError(), new System.Text.UTF8Encoding(false)) { AutoFlush = true });
    }

    private static bool HasUsableStandardOutput()
    {
        var handle = GetStdHandle(StdOutputHandle);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;
        return GetFileType(handle) != FileTypeUnknown;
    }

    private const int StdOutputHandle = -11;
    private const uint FileTypeUnknown = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();
}
