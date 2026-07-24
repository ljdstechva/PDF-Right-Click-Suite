namespace PdfRightClickSuite.Tests;

public sealed class ShellExtensionSourceTests
{
    [Fact]
    public void Native_shell_extension_writes_diagnostic_log_entries()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "native", "PdfRightClickSuite.ShellExtension", "PdfRightClickSuiteShellExtension.cpp"));

        Assert.Contains("shell-extension.log", source, StringComparison.Ordinal);
        Assert.Contains("selected count", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CreateProcessW", source, StringComparison.Ordinal);
        Assert.Contains("launch", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Native_shell_extension_uses_a_sized_utf8_buffer_and_cleans_failed_launch_requests()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "native", "PdfRightClickSuite.ShellExtension", "PdfRightClickSuiteShellExtension.cpp"));

        Assert.Contains("std::string utf8(static_cast<size_t>(byteCount), '\\0')", source, StringComparison.Ordinal);
        Assert.Contains("if (written != byteCount)", source, StringComparison.Ordinal);
        Assert.Contains("TryDeleteRequestFile(requestPath)", source, StringComparison.Ordinal);
        Assert.True(source.Split("TryDeleteRequestFile(requestPath)", StringSplitOptions.None).Length >= 3,
            "Missing request cleanup on one or more native launch-failure paths.");
    }

    [Fact]
    public void Modern_context_menu_attempt_has_sparse_package_artifacts()
    {
        var root = FindRepoRoot();
        var manifestPath = Path.Combine(root, "modern", "AppxManifest.xml");
        var installModern = Path.Combine(root, "scripts", "install-modern.ps1");
        var uninstallModern = Path.Combine(root, "scripts", "uninstall-modern.ps1");
        var registerModern = Path.Combine(root, "scripts", "register-modern-menu.ps1");

        Assert.True(File.Exists(manifestPath), $"Missing {manifestPath}");
        Assert.True(File.Exists(installModern), $"Missing {installModern}");
        Assert.True(File.Exists(uninstallModern), $"Missing {uninstallModern}");
        Assert.True(File.Exists(registerModern), $"Missing {registerModern}");
        Assert.True(File.Exists(Path.Combine(root, "modern", "Assets", "Logo.png")), "Missing modern package logo asset.");

        var manifest = File.ReadAllText(manifestPath);
        var installScript = File.ReadAllText(registerModern);

        Assert.Contains("windows.fileExplorerContextMenus", manifest, StringComparison.Ordinal);
        Assert.Contains("IExplorerCommand", manifest, StringComparison.Ordinal);
        Assert.Contains("SurrogateServer", manifest, StringComparison.Ordinal);
        Assert.Contains("runFullTrust", manifest, StringComparison.Ordinal);
        Assert.Contains("AllowExternalContent", manifest, StringComparison.Ordinal);
        Assert.Contains("PdfRightClickSuiteOpenWith", manifest, StringComparison.Ordinal);
        Assert.Contains("AD6102B8-2161-44C7-B63A-E93821D6FBC0", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InProcessServer", manifest, StringComparison.Ordinal);
        Assert.Contains("Add-AppxPackage", installScript, StringComparison.Ordinal);
        Assert.Contains("Add-AppxPackage -Path", installScript, StringComparison.Ordinal);
        Assert.Contains("-ExternalLocation", installScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Classic_context_menu_inserts_pdf_menu_at_top_with_final_labels()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "native", "PdfRightClickSuite.ShellExtension", "PdfRightClickSuiteShellExtension.cpp"));

        Assert.Contains("CLSID_PdfRightClickSuiteTop", source, StringComparison.Ordinal);
        Assert.Contains("ExplorerTopCommandHandler", source, StringComparison.Ordinal);
        Assert.Contains("ECF_HASSUBCOMMANDS", source, StringComparison.Ordinal);
        Assert.Contains("EnumSubCommands", source, StringComparison.Ordinal);
        Assert.Contains("classic-top availability", source, StringComparison.Ordinal);
        Assert.Contains("const UINT insertionIndex = 0", source, StringComparison.Ordinal);
        Assert.Contains("InsertMenuItemW(menu, insertionIndex", source, StringComparison.Ordinal);
        Assert.Contains("L\"PDF\"", source, StringComparison.Ordinal);
        Assert.Contains("L\"Convert to PDF\"", source, StringComparison.Ordinal);
        Assert.Contains("L\"Merge PDFs\"", source, StringComparison.Ordinal);
        Assert.Contains("L\"Split PDF\"", source, StringComparison.Ordinal);
        Assert.Contains("L\"Convert PDF To\"", source, StringComparison.Ordinal);
        Assert.Contains("L\"Word (.docx)\"", source, StringComparison.Ordinal);
        Assert.Contains("L\"Excel (.xlsx)\"", source, StringComparison.Ordinal);
        Assert.Contains("L\"PowerPoint (.pptx)\"", source, StringComparison.Ordinal);
        Assert.Contains("L\"Make Scanned PDF (B&W)\"", source, StringComparison.Ordinal);
        Assert.Contains("L\"Make Scanned PDF (Colored)\"", source, StringComparison.Ordinal);
        Assert.Contains("L\"Open PDF With\"", source, StringComparison.Ordinal);
        Assert.Contains("SHAssocEnumHandlers", source, StringComparison.Ordinal);
        Assert.Contains("ASSOC_FILTER_RECOMMENDED", source, StringComparison.Ordinal);
        Assert.Contains("IAssocHandler", source, StringComparison.Ordinal);
        Assert.Contains("OpenPdfWithApp", source, StringComparison.Ordinal);
        Assert.Contains("PdfAppCommandHandler", source, StringComparison.Ordinal);
        Assert.Contains("ShellExecutePdfAppFallback", source, StringComparison.Ordinal);
        Assert.Contains("ShellExecuteW", source, StringComparison.Ordinal);
        Assert.Contains("open-with association invoke failed", source, StringComparison.Ordinal);
        Assert.Contains("open-with fallback ShellExecute succeeded", source, StringComparison.Ordinal);
        Assert.Contains("CommandScanColored", source, StringComparison.Ordinal);
        Assert.Contains("CommandOpenWith", source, StringComparison.Ordinal);
        Assert.Contains("CommandConvertTo = 6", source, StringComparison.Ordinal);
        Assert.Contains("CommandConvertToWord = 7", source, StringComparison.Ordinal);
        Assert.Contains("CommandConvertToExcel = 8", source, StringComparison.Ordinal);
        Assert.Contains("CommandConvertToPowerPoint = 9", source, StringComparison.Ordinal);
        Assert.Contains("CommandCount = 10", source, StringComparison.Ordinal);
        Assert.Contains("scanColored", source, StringComparison.Ordinal);
        Assert.Contains("convertToWord", source, StringComparison.Ordinal);
        Assert.Contains("convertToExcel", source, StringComparison.Ordinal);
        Assert.Contains("convertToPowerPoint", source, StringComparison.Ordinal);
        Assert.Contains("Convert the selected PDF to an Office document", source, StringComparison.Ordinal);
        Assert.Contains("Convert the selected PDF to an editable Word document", source, StringComparison.Ordinal);
        Assert.Contains("Convert the selected PDF to an Excel workbook (best for table-style PDFs)", source, StringComparison.Ordinal);
        Assert.Contains("Convert the selected PDF to a PowerPoint presentation (one slide per page)", source, StringComparison.Ordinal);
        Assert.Contains("{D8A8E1C0-7B67-4F77-9B57-5B074C3A2C8F}", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{AD6102B8-2161-44C7-B63A-E93821D6FBC0}", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{4AA1C5C6-946D-4268-AF0C-8C3C137B0E24}", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{8EA50A51-83A3-453F-8007-C946A13B081F}", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{388E7AA8-AEDA-42C5-9477-0B50F86D4A6C}", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{EF7E97A8-DC06-4309-BCC9-48CA62875387}", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PdfRightClickSuite.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate PdfRightClickSuite.sln.");
    }
}
