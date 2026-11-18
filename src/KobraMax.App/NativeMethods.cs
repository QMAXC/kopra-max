using System.Runtime.InteropServices;

namespace KobraMax.App;

/// <summary>Die paar Windows-Funktionen, die WinUI nicht direkt anbietet.</summary>
internal static partial class NativeMethods
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    /// <summary>Verhindert (true) bzw. erlaubt (false) den Ruhezustand – während eines Drucks wichtig.</summary>
    public static void KeepAwake(bool an) =>
        _ = SetThreadExecutionState(an ? EsContinuous | EsSystemRequired : EsContinuous);

    public static double ScaleFor(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    /// <summary>
    /// Klassischer Windows-Dateidialog. Anders als der WinUI-FileOpenPicker kann er in einem
    /// beliebigen Startordner (z. B. H:\3D) öffnen. Gibt null zurück, wenn abgebrochen wurde.
    /// </summary>
    public static string? OpenFile(IntPtr owner, string? startOrdner, string filter, string titel)
    {
        const int MaxPfad = 32768;
        const uint OfnHideReadOnly = 0x00000004;
        const uint OfnNoChangeDir = 0x00000008;
        const uint OfnPathMustExist = 0x00000800;
        const uint OfnFileMustExist = 0x00001000;
        const uint OfnExplorer = 0x00080000;

        var puffer = Marshal.AllocHGlobal(MaxPfad * sizeof(char));
        var filterPtr = Marshal.StringToHGlobalUni(filter);
        var ordnerPtr = startOrdner is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(startOrdner);
        var titelPtr = Marshal.StringToHGlobalUni(titel);
        try
        {
            Marshal.WriteInt16(puffer, 0);
            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                hwndOwner = owner,
                lpstrFilter = filterPtr,
                nFilterIndex = 1,
                lpstrFile = puffer,
                nMaxFile = MaxPfad,
                lpstrInitialDir = ordnerPtr,
                lpstrTitle = titelPtr,
                Flags = OfnExplorer | OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir | OfnHideReadOnly,
            };
            return GetOpenFileNameW(ref ofn) ? Marshal.PtrToStringUni(puffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(puffer);
            Marshal.FreeHGlobal(filterPtr);
            Marshal.FreeHGlobal(titelPtr);
            if (ordnerPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(ordnerPtr);
            }
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint esFlags);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);

    [LibraryImport("comdlg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOpenFileNameW(ref OpenFileName ofn);

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public IntPtr lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public IntPtr lpstrInitialDir;
        public IntPtr lpstrTitle;
        public uint Flags;
        public ushort nFileOffset;
        public ushort nFileExtension;
        public IntPtr lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }
}
