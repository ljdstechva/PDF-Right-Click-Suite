using PdfRightClickSuite.Core;

namespace PdfRightClickSuite.Tests;

public sealed class LoggerServiceTests
{
    [Fact]
    public void Logger_reports_write_failure_without_throwing_during_error_handling()
    {
        using var temp = new TemporaryDirectory();
        var file = temp.CreateFile("not-a-folder");
        var logger = new LoggerService(Path.Combine(file, "logs"));

        Assert.False(logger.Info("test"));
        Assert.False(logger.Error(new InvalidOperationException("failure"), "context"));
    }

    [Fact]
    public void Logger_writes_timestamped_log_when_folder_is_available()
    {
        using var temp = new TemporaryDirectory();
        var logger = new LoggerService(temp.Path);

        Assert.True(logger.Info("test message"));

        var log = Assert.Single(Directory.GetFiles(temp.Path, "*.log"));
        Assert.Contains("[INFO] test message", File.ReadAllText(log), StringComparison.Ordinal);
    }
}
