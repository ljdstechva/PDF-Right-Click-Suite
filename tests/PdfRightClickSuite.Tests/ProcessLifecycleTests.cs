using System.Diagnostics;
using System.Reflection;
using PdfRightClickSuite.Core;

namespace PdfRightClickSuite.Tests;

public sealed class ProcessLifecycleTests
{
    [Fact]
    public async Task External_converter_process_tree_is_stopped_on_cancellation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TemporaryDirectory();
        var pidPath = Path.Combine(temp.Path, "converter.pid");
        var command = $"$child = Start-Process -FilePath \"$env:WINDIR\\System32\\ping.exe\" -ArgumentList '-t','127.0.0.1' -WindowStyle Hidden -PassThru; \"$PID,$($child.Id)\" | Set-Content -LiteralPath '{EscapePowerShellLiteral(pidPath)}'; Wait-Process -Id $child.Id";
        using var cancellation = new CancellationTokenSource();
        var task = InvokeExternalProcessRunner(
            ["-NoProfile", "-NonInteractive", "-Command", command],
            TimeSpan.FromMinutes(1),
            cancellation.Token);

        var parentPid = 0;
        var childPid = 0;
        try
        {
            await WaitUntilAsync(
                () => TryReadProcessIds(pidPath, out parentPid, out childPid),
                TimeSpan.FromSeconds(10),
                "External converter did not start.");
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
            await WaitUntilAsync(
                () => HasExited(parentPid) && HasExited(childPid),
                TimeSpan.FromSeconds(10),
                "Cancelled external converter process tree was still running.");
        }
        finally
        {
            TryKill(childPid);
            TryKill(parentPid);
        }
    }

    [Fact]
    public async Task External_converter_timeout_stops_process_and_reports_timeout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TemporaryDirectory();
        var executableName = $"pdf-converter-timeout-{Guid.NewGuid():N}";
        var executablePath = Path.Combine(temp.Path, executableName + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), executablePath);
        var task = InvokeExternalProcessRunner(
            executablePath,
            ["-t", "127.0.0.1"],
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        var pid = 0;
        try
        {
            await WaitUntilAsync(
                () => GetProcessIds(executableName).Count > 0,
                TimeSpan.FromSeconds(5),
                "External converter did not start.");
            pid = Assert.Single(GetProcessIds(executableName));

            var exception = await Assert.ThrowsAsync<TimeoutException>(async () => await task);
            Assert.Contains("did not finish", exception.Message, StringComparison.OrdinalIgnoreCase);
            await WaitUntilAsync(() => HasExited(pid), TimeSpan.FromSeconds(10), "Timed-out external converter was still running.");
        }
        finally
        {
            TryKill(pid);
        }
    }

    [Fact]
    public async Task Cancelled_office_worker_runs_deferred_cleanup_after_it_finishes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var method = typeof(MicrosoftOfficePdfConverter).GetMethod("RunOnStaThread", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        using var cancellation = new CancellationTokenSource();
        using var workStarted = new ManualResetEventSlim();
        using var releaseWork = new ManualResetEventSlim();
        var cleanupCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action work = () =>
        {
            workStarted.Set();
            releaseWork.Wait();
        };
        Action cleanup = () => cleanupCompleted.TrySetResult();

        var invocation = Task.Run(() => method.Invoke(
            null,
            [work, TimeSpan.FromSeconds(10), cancellation.Token, cleanup]));

        try
        {
            Assert.True(workStarted.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
            cancellation.Cancel();

            var exception = await Assert.ThrowsAsync<TargetInvocationException>(async () =>
                await invocation.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
            Assert.IsType<OperationCanceledException>(exception.InnerException);
            Assert.False(cleanupCompleted.Task.IsCompleted);

            releaseWork.Set();
            await cleanupCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        }
        finally
        {
            releaseWork.Set();
        }
    }

    private static Task InvokeExternalProcessRunner(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        => InvokeExternalProcessRunner("powershell.exe", arguments, timeout, cancellationToken);

    private static Task InvokeExternalProcessRunner(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var method = typeof(PdfConvertService).GetMethod("RunProcessAsync", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (Task)method.Invoke(null, [executablePath, arguments, timeout, cancellationToken])!;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string failureMessage)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(condition(), failureMessage);
    }

    private static bool HasExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static IReadOnlyList<int> GetProcessIds(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Select(process => process.Id).ToArray();
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static bool TryReadProcessIds(string path, out int parentProcessId, out int childProcessId)
    {
        parentProcessId = 0;
        childProcessId = 0;
        try
        {
            var parts = File.ReadAllText(path).Trim().Split(',');
            return parts.Length == 2
                && int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out parentProcessId)
                && int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out childProcessId);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void TryKill(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch (ArgumentException ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
