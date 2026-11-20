using Windows.UI;
using Windows.UI.ViewManagement;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;

namespace GalgameManager.Helpers;

// Helper class to workaround custom title bar bugs.
// DISCLAIMER: The resource key names and color values used below are subject to change. Do not depend on them.
// https://github.com/microsoft/TemplateStudio/issues/4516
internal class TitleBarHelper
{
    public static void UpdateTitleBar(ElementTheme theme)
    {
        if (App.MainWindow!.ExtendsContentIntoTitleBar)
        {
            if (theme == ElementTheme.Default)
            {
                var uiSettings = new UISettings();
                var background = uiSettings.GetColorValue(UIColorType.Background);

                theme = background == Colors.White ? ElementTheme.Light : ElementTheme.Dark;
            }

            if (theme == ElementTheme.Default)
            {
                theme = Application.Current.RequestedTheme == ApplicationTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
            }

            // 使用新的直接设置标题栏颜色的方法
            SetTitleBarTheme(theme);
        }
    }

    /// <summary>
    /// 设置标题栏按钮的主题色
    /// </summary>
    private static void SetTitleBarTheme(ElementTheme theme)
    {
        if (App.MainWindow == null) return;
        
        var appWindow = App.MainWindow.AppWindow;
        if (appWindow == null) return;
        
        AppWindowTitleBar titleBar = appWindow.TitleBar;

        titleBar.BackgroundColor = Colors.Transparent;
        titleBar.ForegroundColor = Colors.Transparent;
        titleBar.InactiveBackgroundColor = Colors.Transparent;
        titleBar.InactiveForegroundColor = Colors.Transparent;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        
        if (theme is ElementTheme.Light)
        {
            titleBar.ButtonForegroundColor = Color.FromArgb(255, 23, 23, 23);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(25, 0, 0, 0);
            titleBar.ButtonHoverForegroundColor = Colors.Black;
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(51, 0, 0, 0);
            titleBar.ButtonPressedForegroundColor = Colors.Black;
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 153, 153, 153);
        }
        else
        {
            titleBar.ButtonForegroundColor = Color.FromArgb(255, 242, 242, 242);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(25, 255, 255, 255);
            titleBar.ButtonHoverForegroundColor = Colors.White;
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(51, 255, 255, 255);
            titleBar.ButtonPressedForegroundColor = Colors.White;
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 102, 102, 102);
        }
    }
}
