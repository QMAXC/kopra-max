using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace KobraMax.App;

/// <summary>
/// Legt beim ersten Start die Verknüpfung „Kobra Max Steuerung" auf dem Desktop an.
/// Ohne PowerShell (auf Schul-PCs oft gesperrt) – direkt über die Windows-Shell (IShellLink).
/// Eine Merk-Datei im App-Ordner verhindert, dass eine gelöschte Verknüpfung immer wieder auftaucht.
/// </summary>
internal static class DesktopShortcut
{
    public const string Name = "Kobra Max Steuerung.lnk";
    private const string MerkDatei = "desktop-verknuepfung.ok";

    /// <summary>Gibt den Pfad der neuen Verknüpfung zurück, oder null, wenn nichts zu tun war.</summary>
    public static string? EnsureOnce()
    {
        var exe = Environment.ProcessPath;
        var ordner = exe is null ? null : Path.GetDirectoryName(exe);
        if (exe is null || ordner is null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var merk = Path.Combine(ordner, MerkDatei);
        if (File.Exists(merk))
        {
            return null;
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
        {
            return null;
        }

        var lnk = Path.Combine(desktop, Name);
        Create(lnk, exe, ordner);
        File.WriteAllText(merk, lnk);
        return lnk;
    }

    private static void Create(string lnk, string ziel, string arbeitsordner)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(ziel);
            link.SetWorkingDirectory(arbeitsordner);
            link.SetIconLocation(ziel, 0);
            link.SetDescription("Anycubic Kobra Max über USB steuern");
            ((IPersistFile)link).Save(lnk, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    // Reihenfolge = Vtable von IShellLinkW (shobjidl_core.h) – nicht umsortieren!
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(IntPtr pszFile, int cch, IntPtr pfd, uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription(IntPtr pszName, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory(IntPtr pszDir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments(IntPtr pszArgs, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation(IntPtr pszIconPath, int cch, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
