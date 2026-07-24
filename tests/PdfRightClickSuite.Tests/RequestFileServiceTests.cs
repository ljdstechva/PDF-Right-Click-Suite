using PdfRightClickSuite.Core;

namespace PdfRightClickSuite.Tests;

public sealed class RequestFileServiceTests
{
    [Fact]
    public void Request_json_round_trips_paths_with_spaces_and_unicode()
    {
        using var temp = new TemporaryDirectory();
        var service = new RequestFileService();
        var path = Path.Combine(temp.Path, "request.json");
        var request = new PdfRequest(
            PdfAction.Merge,
            [Path.Combine(temp.Path, "Résumé 1.pdf"), Path.Combine(temp.Path, "file two.pdf")],
            new DateTimeOffset(2026, 7, 1, 5, 10, 11, TimeSpan.Zero),
            temp.Path,
            "req-123");

        service.Write(path, request);
        var roundTrip = service.Read(path);

        Assert.Equal(request, roundTrip);
    }

    [Fact]
    public void Request_json_round_trips_colored_scan_action_name()
    {
        using var temp = new TemporaryDirectory();
        var service = new RequestFileService();
        var path = Path.Combine(temp.Path, "request.json");
        var request = new PdfRequest(
            PdfAction.ScanColored,
            [Path.Combine(temp.Path, "source.pdf")],
            new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero),
            temp.Path,
            "req-scan-colored");

        service.Write(path, request);
        var json = File.ReadAllText(path);
        var roundTrip = service.Read(path);

        Assert.Contains("\"action\": \"scanColored\"", json, StringComparison.Ordinal);
        Assert.Equal(request, roundTrip);
    }

    [Theory]
    [InlineData(PdfAction.ConvertToWord, "convertToWord")]
    [InlineData(PdfAction.ConvertToExcel, "convertToExcel")]
    [InlineData(PdfAction.ConvertToPowerPoint, "convertToPowerPoint")]
    public void Request_json_round_trips_pdf_to_office_action_names(PdfAction action, string literal)
    {
        using var temp = new TemporaryDirectory();
        var service = new RequestFileService();
        var path = Path.Combine(temp.Path, "request.json");
        var request = new PdfRequest(
            action,
            [Path.Combine(temp.Path, "source.pdf")],
            new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero),
            temp.Path,
            $"req-{literal}");

        service.Write(path, request);
        var json = File.ReadAllText(path);
        var roundTrip = service.Read(path);

        Assert.Contains($"\"action\": \"{literal}\"", json, StringComparison.Ordinal);
        Assert.Equal(request, roundTrip);
    }

    [Fact]
    public void Raw_convert_to_word_action_literal_deserializes_to_appended_enum_value()
    {
        using var temp = new TemporaryDirectory();
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            $$"""
              {
                "action": "convertToWord",
                "selectedFiles": ["{{Path.Combine(temp.Path, "source.pdf").Replace("\\", "\\\\", StringComparison.Ordinal)}}"],
                "timestampUtc": "2026-07-24T12:00:00+00:00",
                "sourceFolder": "{{temp.Path.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                "requestId": "raw-convert-to-word"
              }
              """);

        var request = new RequestFileService().Read(requestPath);

        Assert.Equal(PdfAction.ConvertToWord, request.Action);
    }

    [Fact]
    public void Read_rejects_oversized_requests()
    {
        using var temp = new TemporaryDirectory();
        var path = temp.CreateFile("oversized.json", new string('x', checked((int)RequestFileService.MaxRequestBytes + 1)));

        var exception = Assert.Throws<InvalidDataException>(() => new RequestFileService().Read(path));

        Assert.Contains("limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_rejects_requests_without_selected_files()
    {
        using var temp = new TemporaryDirectory();
        var service = new RequestFileService();
        var path = Path.Combine(temp.Path, "empty-selection.json");
        File.WriteAllText(path, """
            {
              "action": "merge",
              "selectedFiles": [],
              "invokedAt": "2026-07-13T00:00:00+00:00",
              "explorerFolder": null,
              "requestId": "empty-selection"
            }
            """);

        var exception = Assert.Throws<InvalidDataException>(() => service.Read(path));

        Assert.Contains("selected files", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Consume_shell_request_deletes_it_after_reading()
    {
        using var temp = new TemporaryDirectory();
        var shellRoot = Path.Combine(temp.Path, "shell-requests");
        var service = new RequestFileService(shellRoot);
        var path = Path.Combine(shellRoot, $"request-{Guid.NewGuid():N}.json");
        var request = new PdfRequest(
            PdfAction.Split,
            [Path.Combine(temp.Path, "source.pdf")],
            DateTimeOffset.UtcNow,
            temp.Path,
            Guid.NewGuid().ToString("N"));
        service.Write(path, request);

        var consumed = service.ConsumeShellRequest(path);

        Assert.Equal(request, consumed);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Consume_shell_request_deletes_malformed_payload_but_never_arbitrary_request_files()
    {
        using var temp = new TemporaryDirectory();
        var shellRoot = Path.Combine(temp.Path, "shell-requests");
        Directory.CreateDirectory(shellRoot);
        var service = new RequestFileService(shellRoot);
        var shellPath = Path.Combine(shellRoot, $"request-{Guid.NewGuid():N}.json");
        var arbitraryPath = temp.CreateFile("request-user-owned.json", "not json");
        File.WriteAllText(shellPath, "not json");

        Assert.Throws<InvalidDataException>(() => service.ConsumeShellRequest(shellPath));

        Assert.False(File.Exists(shellPath));
        Assert.False(service.IsShellRequestPath(arbitraryPath));
        Assert.Throws<ArgumentException>(() => service.ConsumeShellRequest(arbitraryPath));
        Assert.True(File.Exists(arbitraryPath));
    }

    [Fact]
    public void Cleanup_stale_shell_requests_only_removes_expired_owned_files()
    {
        using var temp = new TemporaryDirectory();
        var shellRoot = Path.Combine(temp.Path, "shell-requests");
        Directory.CreateDirectory(shellRoot);
        var service = new RequestFileService(shellRoot);
        var stalePath = Path.Combine(shellRoot, $"request-{Guid.NewGuid():N}.json");
        var currentPath = Path.Combine(shellRoot, $"request-{Guid.NewGuid():N}.json");
        var unrelatedPath = Path.Combine(shellRoot, "notes.json");
        File.WriteAllText(stalePath, "{}");
        File.WriteAllText(currentPath, "{}");
        File.WriteAllText(unrelatedPath, "{}");
        File.SetLastWriteTimeUtc(stalePath, DateTime.UtcNow.AddHours(-2));

        var deleted = service.CleanupStaleShellRequests(TimeSpan.FromHours(1));

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(stalePath));
        Assert.True(File.Exists(currentPath));
        Assert.True(File.Exists(unrelatedPath));
    }
}
