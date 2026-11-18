using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class WindowsInstallation
{
    // Matches the product code in BuildInstaller.ps1.
    private const string ProductCode = "{0B823DBB-46A2-4693-95A8-F3E74FDEFD2B}";
    private const int InstalledLocally = 5;

    public static string ApplicationPath
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "KeyDebounce", "KeyDebounce.exe");
        }
    }

    public static bool IsInstalled
    {
        get { return File.Exists(ApplicationPath) && MsiQueryProductState(ProductCode) == InstalledLocally; }
    }

    private static string StartupLink
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "KeyDebounce.lnk"); }
    }

    public static bool StartsWithWindows
    {
        get { return File.Exists(StartupLink); }
    }

    public static void SetStartsWithWindows(bool enabled)
    {
        string path = StartupLink;
        if (!enabled)
        {
            File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
        dynamic link = null;
        try
        {
            link = shell.CreateShortcut(path);
            link.TargetPath = ApplicationPath;
            link.Arguments = "--startup";
            link.WorkingDirectory = Path.GetDirectoryName(ApplicationPath);
            link.Description = "Start KeyDebounce at sign-in";
            link.Save();
        }
        finally
        {
            if (link != null)
                Marshal.FinalReleaseComObject((object)link);
            Marshal.FinalReleaseComObject((object)shell);
        }
    }

    public static void Uninstall()
    {
        using (Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
            "/x " + ProductCode + " /passive /norestart") { UseShellExecute = false }))
        {
            // The settings window exits immediately after launching Windows Installer.
        }
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern int MsiQueryProductState(string productCode);
}
