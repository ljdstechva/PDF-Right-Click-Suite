namespace PdfRightClickSuite.Tests;

public sealed class InstallerScriptTests
{
    [Fact]
    public void Install_script_builds_default_release_when_binaries_are_missing()
    {
        var installScript = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "install.ps1"));

        Assert.Contains("function Ensure-ReleaseReady", installScript, StringComparison.Ordinal);
        Assert.Contains("build-release.ps1", installScript, StringComparison.Ordinal);
        Assert.Contains("Ensure-ReleaseReady", installScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_and_uninstall_scripts_capture_regsvr32_exit_codes()
    {
        var root = FindRepoRoot();
        var installScript = File.ReadAllText(Path.Combine(root, "scripts", "install.ps1"));
        var uninstallScript = File.ReadAllText(Path.Combine(root, "scripts", "uninstall.ps1"));

        Assert.Contains("Start-Process", installScript, StringComparison.Ordinal);
        Assert.Contains("-Wait", installScript, StringComparison.Ordinal);
        Assert.Contains("-PassThru", installScript, StringComparison.Ordinal);
        Assert.Contains("Start-Process", uninstallScript, StringComparison.Ordinal);
        Assert.Contains("-Wait", uninstallScript, StringComparison.Ordinal);
        Assert.Contains("-PassThru", uninstallScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Uninstall_script_does_not_delete_its_own_running_directory()
    {
        var uninstallScript = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "uninstall.ps1"));

        Assert.Contains("PathIsUnder", uninstallScript, StringComparison.Ordinal);
        Assert.Contains("running from inside InstallDir", uninstallScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("installer uninstaller will remove files", uninstallScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Install_script_notifies_shell_and_registers_approved_extension()
    {
        var installScript = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "install.ps1"));

        Assert.Contains("Shell Extensions\\Approved", installScript, StringComparison.Ordinal);
        Assert.Contains("SHChangeNotify", installScript, StringComparison.Ordinal);
        Assert.Contains("SHCNE_ASSOCCHANGED", installScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_release_packages_install_scripts()
    {
        var buildScript = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "build-release.ps1"));

        Assert.Contains("install.ps1", buildScript, StringComparison.Ordinal);
        Assert.Contains("uninstall.ps1", buildScript, StringComparison.Ordinal);
        Assert.Contains("install-classic-top-menu.ps1", buildScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("uninstall-classic-top-menu.ps1", buildScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("audit-classic-menu-order.ps1", buildScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("audit-startup-silence.ps1", buildScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disable-pdfgear-context-menu.ps1", buildScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restore-pdfgear-context-menu.ps1", buildScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("optional-modern-menu", buildScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scripts", buildScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Transferable_installer_script_registers_shell_extension()
    {
        var installerScript = File.ReadAllText(Path.Combine(FindRepoRoot(), "installer", "PdfRightClickSuite.iss"));

        Assert.Contains("PdfRightClickSuiteSetup", installerScript, StringComparison.Ordinal);
        Assert.Contains("install.ps1", installerScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("uninstall.ps1", installerScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("regserver", installerScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Installer_does_not_create_windows_startup_launch_points()
    {
        var installerScript = File.ReadAllText(Path.Combine(FindRepoRoot(), "installer", "PdfRightClickSuite.iss"));

        Assert.DoesNotContain(@"{userstartup}", installerScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"{commonstartup}", installerScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"CurrentVersion\Run", installerScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tasks:", installerScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_silence_audit_is_opt_in_and_scoped_to_suite_noise()
    {
        var root = FindRepoRoot();
        var scriptPath = Path.Combine(root, "scripts", "audit-startup-silence.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.True(File.Exists(scriptPath), $"Missing {scriptPath}");
        Assert.Contains("[switch]$Apply", script, StringComparison.Ordinal);
        Assert.Contains("Test-StartupCommandIsPdfRightClickSuiteNoise", script, StringComparison.Ordinal);
        Assert.Contains("PdfRightClickSuite.Cli.exe", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--diagnose", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--self-test", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-StartupApprovedEntries", script, StringComparison.Ordinal);
        Assert.Contains("Get-StartMenuShortcutEntries", script, StringComparison.Ordinal);
        Assert.Contains("InventoryOnly", script, StringComparison.Ordinal);
        Assert.Contains("PotentialNoise", script, StringComparison.Ordinal);
        Assert.Contains("reg.exe export", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Export-ScheduledTask", script, StringComparison.Ordinal);
        Assert.Contains("Disable-ScheduledTask", script, StringComparison.Ordinal);
        Assert.Contains("if ($Apply)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PDFgear.exe", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Install_script_registers_classic_top_shell_verb()
    {
        var root = FindRepoRoot();
        var installScript = File.ReadAllText(Path.Combine(root, "scripts", "install.ps1"));
        var uninstallScript = File.ReadAllText(Path.Combine(root, "scripts", "uninstall.ps1"));
        var topInstallScript = File.ReadAllText(Path.Combine(root, "scripts", "install-classic-top-menu.ps1"));
        var topUninstallScript = File.ReadAllText(Path.Combine(root, "scripts", "uninstall-classic-top-menu.ps1"));

        Assert.Contains("install-classic-top-menu.ps1", installScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Classic top-menu registry key", installScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ExplorerCommandHandler", topInstallScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{065E1050-7F50-4FDF-94C6-19B998E64A83}", topInstallScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Position", topInstallScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Top", topInstallScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MultiSelectModel", topInstallScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Player", topInstallScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("assets\\pdf.ico", topInstallScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ClassicTopMenuIconPath", topInstallScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ContextMenuHandlers\\PdfRightClickSuite", topInstallScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("uninstall-classic-top-menu.ps1", uninstallScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{AD6102B8-2161-44C7-B63A-E93821D6FBC0}", uninstallScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Remove-Item", topUninstallScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Release_packages_dedicated_pdf_menu_icon()
    {
        var root = FindRepoRoot();
        var iconPath = Path.Combine(root, "assets", "icons", "pdf.ico");
        var buildScript = File.ReadAllText(Path.Combine(root, "scripts", "build-release.ps1"));
        var auditScript = File.ReadAllText(Path.Combine(root, "scripts", "audit-classic-menu-order.ps1"));
        var nativeSource = File.ReadAllText(Path.Combine(root, "native", "PdfRightClickSuite.ShellExtension", "PdfRightClickSuiteShellExtension.cpp"));

        Assert.True(File.Exists(iconPath), $"Missing {iconPath}");
        Assert.Contains("assets\\icons\\pdf.ico", buildScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("assets\\pdf.ico", buildScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PdfMenuIconExists", auditScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MenuIconPath", nativeSource, StringComparison.Ordinal);
        Assert.Contains("\\\\assets\\\\pdf.ico", nativeSource, StringComparison.Ordinal);
        AssertIcoContainsMultipleImages(iconPath);
    }

    [Fact]
    public void Install_script_skips_copy_when_source_and_install_dir_are_same()
    {
        var installScript = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "install.ps1"));

        Assert.Contains("SourcePath and InstallDir are the same", installScript, StringComparison.Ordinal);
        Assert.Contains("PathEquals", installScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_and_uninstall_scripts_keep_modern_menu_out_of_default_flow()
    {
        var root = FindRepoRoot();
        var installScript = File.ReadAllText(Path.Combine(root, "scripts", "install.ps1"));
        var uninstallScript = File.ReadAllText(Path.Combine(root, "scripts", "uninstall.ps1"));

        Assert.DoesNotContain("Register-ModernMenu", installScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Add-AppxPackage", installScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TrustedPeople", installScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Modern AppX/MSIX context-menu registration is intentionally disabled", installScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Remove-StaleModernMenuInstallArtifacts", installScript, StringComparison.Ordinal);
        Assert.Contains("scripts\\register-modern-menu.ps1", installScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unregister-modern-menu.ps1", uninstallScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove-AppxPackage", uninstallScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Modern AppX/MSIX context-menu unregister is intentionally not part", uninstallScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Install_script_disables_pdfgear_context_menu_with_restore_script_available()
    {
        var root = FindRepoRoot();
        var installScript = File.ReadAllText(Path.Combine(root, "scripts", "install.ps1"));
        var disableScriptPath = Path.Combine(root, "scripts", "disable-pdfgear-context-menu.ps1");
        var restoreScriptPath = Path.Combine(root, "scripts", "restore-pdfgear-context-menu.ps1");
        var disableScript = File.ReadAllText(disableScriptPath);
        var restoreScript = File.ReadAllText(restoreScriptPath);

        Assert.Contains("disable-pdfgear-context-menu.ps1", installScript, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(disableScriptPath), $"Missing {disableScriptPath}");
        Assert.True(File.Exists(restoreScriptPath), $"Missing {restoreScriptPath}");
        Assert.Contains("registry-backups", disableScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reg.exe export", disableScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LegacyDisable", disableScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ContextMenuHandlers", disableScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pdfgear-disable-manifest", disableScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pdfgear-disable-manifest", restoreScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Restore-Value", restoreScript, StringComparison.Ordinal);
        Assert.DoesNotContain("catch {\r\n        }", restoreScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Modern_registration_scripts_manage_certificate_and_package()
    {
        var root = FindRepoRoot();
        var registerScript = File.ReadAllText(Path.Combine(root, "scripts", "register-modern-menu.ps1"));
        var registerElevatedScript = File.ReadAllText(Path.Combine(root, "scripts", "register-modern-menu-elevated.ps1"));
        var unregisterScript = File.ReadAllText(Path.Combine(root, "scripts", "unregister-modern-menu.ps1"));
        var testScript = File.ReadAllText(Path.Combine(root, "scripts", "test-modern-menu.ps1"));

        Assert.Contains("Get-AuthenticodeSignature", registerScript, StringComparison.Ordinal);
        Assert.Contains("TrustedPeople", registerScript, StringComparison.Ordinal);
        Assert.Contains("StoreName]::Root", registerScript, StringComparison.Ordinal);
        Assert.Contains("LocalMachine\\TrustedPeople", registerScript, StringComparison.Ordinal);
        Assert.Contains("certutil.exe -addstore TrustedPeople", registerScript, StringComparison.Ordinal);
        Assert.Contains("Add-AppxPackage", registerScript, StringComparison.Ordinal);
        Assert.Contains("ModernCertificateThumbprint", registerScript, StringComparison.Ordinal);
        Assert.Contains("ModernCertificateMachineTrustedPeopleImportedByScript", registerScript, StringComparison.Ordinal);
        Assert.Contains("Start-Process", registerElevatedScript, StringComparison.Ordinal);
        Assert.Contains("-Verb RunAs", registerElevatedScript, StringComparison.Ordinal);
        Assert.Contains("LocalMachine\\TrustedPeople", registerElevatedScript, StringComparison.Ordinal);
        Assert.Contains("Remove-AppxPackage", unregisterScript, StringComparison.Ordinal);
        Assert.Contains("ModernCertificateTrustedPeopleImportedByScript", unregisterScript, StringComparison.Ordinal);
        Assert.Contains("ModernCertificateMachineTrustedPeopleImportedByScript", unregisterScript, StringComparison.Ordinal);
        Assert.Contains("ModernPackageRegistered", testScript, StringComparison.Ordinal);
        Assert.Contains("CertificateMachineTrustedPeople", testScript, StringComparison.Ordinal);
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

    private static void AssertIcoContainsMultipleImages(string iconPath)
    {
        using var stream = File.OpenRead(iconPath);
        using var reader = new BinaryReader(stream);

        Assert.Equal((ushort)0, reader.ReadUInt16());
        Assert.Equal((ushort)1, reader.ReadUInt16());
        var count = reader.ReadUInt16();
        Assert.True(count >= 3, $"Expected at least 3 icon sizes, found {count}.");
    }
}
