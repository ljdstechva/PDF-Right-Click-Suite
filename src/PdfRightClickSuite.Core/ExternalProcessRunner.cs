using System.ComponentModel;
using System.Diagnostics;

namespace PdfRightClickSuite.Core;

internal sealed record ExternalProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class ExternalProcessRunner
{
    public static async Task<ExternalProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        using var processJob = WindowsProcessJob.TryAssign(process);
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await TerminateProcessAsync(process, processJob, stdoutTask, stderrTask).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"External converter '{Path.GetFileName(fileName)}' did not finish within {timeout.TotalSeconds:0} seconds.");
        }

        return new ExternalProcessResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    private static async Task TerminateProcessAsync(
        Process process,
        WindowsProcessJob? processJob,
        Task<string> stdoutTask,
        Task<string> stderrTask)
    {
        processJob?.Dispose();
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            Trace.TraceWarning($"Could not stop external converter process: {ex.Message}");
        }

        try
        {
            using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
        {
            Trace.TraceWarning($"External converter process did not confirm termination: {ex.Message}");
        }

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or TimeoutException)
        {
            Trace.TraceWarning($"Could not finish reading external converter output: {ex.Message}");
        }
    }
}
