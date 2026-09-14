using System.Runtime.InteropServices;
using GalgameManager.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace GalgameManager;

public static class BootstrapProgram
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
#if !MICROSOFT_WINDOWSAPPSDK_SELFCONTAINED
        // 仅框架依赖模式需要调用 Bootstrapper API；自包含模式跳过，直接使用应用目录里的 WinAppSDK 运行时
        if (!RuntimeHelper.IsMSIX)
        {
            try
            {
                // 与 GalgameManager.WinApp.Base.csproj 中的 Microsoft.WindowsAppSDK 版本保持一致
                // Microsoft.WindowsAppSDK 2.1.3 => 0x0002_0001
                Bootstrap.Initialize(0x00020001);
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    try
                    {
                        Bootstrap.Shutdown();
                    }
                    catch
                    {
                        // ignored
                    }
                };
            }
            catch (Exception ex)
            {
                var msg =
                    "未能初始化 Windows App SDK 运行时（免安装/便携模式需要安装 Windows App Runtime）。\n\n" +
                    $"错误：{ex.GetType().Name}: {ex.Message}\n\n" +
                    "请安装/修复 Windows App Runtime（x64）后再启动。";
                try
                {
                    MessageBoxW(IntPtr.Zero, msg, "PotatoVN 启动失败", 0);
                }
                catch
                {
                    // ignored
                }

#if DEBUG
                try
                {
                    var logPath = Path.Combine(AppContext.BaseDirectory, "winappsdk_bootstrap.log");
                    File.AppendAllText(logPath,
                        $"[{DateTimeOffset.Now:O}] Bootstrap.Initialize failed: {ex}{Environment.NewLine}");
                }
                catch
                {
                    // ignored
                }
#endif

                return;
            }
        }
#endif

        Application.Start((p) =>
        {
            DispatcherQueueSynchronizationContext context = new(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
