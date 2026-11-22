using System.Runtime.InteropServices;
using System.Text;

namespace GalgameManager.Helpers;

/// <summary>
/// 用显式 <c>[ComImport]</c> 的 <c>IShellLinkW</c> + <c>IPersistFile</c> 创建 <c>.lnk</c> 快捷方式。<br/>
/// 之所以不用 <c>dynamic</c> 调 <c>WScript.Shell</c>：那需要 DLR 在运行期生成 IL 并对 COM 对象做
/// IDispatch 后期绑定，在 <c>PublishTrimmed=true</c> 下不可靠——便携版点「创建桌面快捷方式」
/// 直接闪退就是这个原因（ILLink 对 dynamic 的调用点会报 IL2026/IL2072）。<br/>
/// 显式 ComImport 接口不产生任何运行期动态代码，且 <c>BuiltInComInteropSupport=true</c> 已开启，
/// 零第三方库依赖。
/// </summary>
public static class DesktopShortcutHelper
{
    private const int SOk = 0;
    private const int SFalse = 1;
    private const uint CoinitMultithreaded = 0x0;

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")] // CLSID_ShellLink
    private class ShellLinkCoClass;

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")] // IID_IShellLinkW
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch,
            ref WIN32_FIND_DATAW pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch,
            out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")] // IID_IPersistFile
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName,
            [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public long ftCreationTime;
        public long ftLastAccessTime;
        public long ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
    }

    [DllImport("ole32.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();

    /// <summary>
    /// 在 <paramref name="shortcutPath"/> 创建一个指向 <paramref name="targetPath"/> 的快捷方式。<br/>
    /// 目标已存在时直接覆盖。任何 COM 失败都会以异常形式抛出，由调用方决定如何提示。
    /// </summary>
    /// <param name="shortcutPath">.lnk 完整路径</param>
    /// <param name="targetPath">快捷方式指向的可执行文件</param>
    /// <param name="iconLocation">图标位置，形如 <c>C:\path\app.ico</c>；为空则用目标自身图标</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="description">鼠标悬停说明</param>
    public static void Create(string shortcutPath, string targetPath, string? iconLocation = null,
        string? workingDirectory = null, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        // 确保当前线程已初始化 COM。Task.Run 会落到线程池线程，CLR 会把它初始化为 MTA，
        // 此时 CoInitializeEx 返回 S_FALSE；只有拿到 S_OK 才说明是我们自己初始化的，
        // 结束时才允许 CoUninitialize，否则会把别人的初始化计数减掉。
        int hr = CoInitializeEx(IntPtr.Zero, CoinitMultithreaded);
        bool weInitializedCom = hr == SOk;
        try
        {
            IShellLinkW link = (IShellLinkW)new ShellLinkCoClass();
            link.SetPath(targetPath);
            if (!string.IsNullOrWhiteSpace(workingDirectory))
                link.SetWorkingDirectory(workingDirectory);
            if (!string.IsNullOrWhiteSpace(description))
                link.SetDescription(description);
            if (!string.IsNullOrWhiteSpace(iconLocation))
                link.SetIconLocation(iconLocation, 0);
            ((IPersistFile)link).Save(shortcutPath, true);
        }
        finally
        {
            if (weInitializedCom) CoUninitialize();
        }
    }

    /// <summary>
    /// 读取已有快捷方式指向的目标路径；文件不存在或不是合法 .lnk 时返回 null。
    /// 仅供测试与诊断使用。
    /// </summary>
    public static string? TryGetTargetPath(string shortcutPath)
    {
        if (!File.Exists(shortcutPath)) return null;
        int hr = CoInitializeEx(IntPtr.Zero, CoinitMultithreaded);
        bool weInitializedCom = hr == SOk;
        try
        {
            IShellLinkW link = (IShellLinkW)new ShellLinkCoClass();
            ((IPersistFile)link).Load(shortcutPath, 0);
            StringBuilder buffer = new(260);
            WIN32_FIND_DATAW findData = new();
            link.GetPath(buffer, buffer.Capacity, ref findData, 0);
            return buffer.Length > 0 ? buffer.ToString() : null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (weInitializedCom) CoUninitialize();
        }
    }
}
