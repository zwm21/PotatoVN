using GalgameManager.Helpers;

namespace GalgameManager.Views;

/// <summary>
/// 完整备份/恢复的进度窗口。<br/>
/// 与 <see cref="ImportWindow"/> 不同，本窗口不依赖任何服务：完整备份必须在
/// <see cref="App.OnLaunched"/> 解析服务之前执行（运行中的实例独占两个 LiteDB 数据库），
/// 因此不能注入 ILocalSettingsService。
/// </summary>
public sealed partial class FullBackupWindow
{
    public FullBackupWindow()
    {
        InitializeComponent();
        TitleTextBlock.Text = "FullBackup_WindowTitle".GetLocalized();
    }

    public void SetStatus(string messageKey, string? messageArg = null)
    {
        StatusTextBlock.Text = messageArg is null
            ? messageKey.GetLocalized()
            : messageKey.GetLocalized(messageArg);
    }
}
