using System.Reflection;
using System.Text;

namespace GalgameManager.Helpers;

/// <summary>
/// 动态版本入口：构建期把 git 采集结果写入嵌入资源 <see cref="ResourceName"/>，运行期只读该资源，
/// 不在运行时调用 git。资源缺失或损坏时回退到 <c>+src.unknown</c>，任何情况下都不抛异常。<br/>
/// 资源行的生成逻辑见 <c>BuildScripts/Get-BuildVersionInfo.ps1</c>，格式说明见 <see cref="AppVersionInfo"/>。
/// </summary>
public static class AppVersion
{
    /// <summary>嵌入资源的逻辑名，需与 GalgameManager.csproj 中 GenerateBuildVersionInfo 目标一致</summary>
    public const string ResourceName = "GalgameManager.BuildVersionInfo.txt";

    /// <summary>当前构建的动态版本</summary>
    public static AppVersionInfo Current { get; } = Load();

    /// <summary>日志首行使用的版本串</summary>
    public static string LogLine => Current.ToLogLine();

    /// <summary>UI 显示的版本串</summary>
    public static string DisplayVersion => Current.ToDisplayVersion();

    /// <summary>从资源文本构造；资源缺失或损坏时回退，不抛异常。</summary>
    public static AppVersionInfo LoadFrom(string? resourceText, string baseVersion)
    {
        return AppVersionInfo.TryParse(resourceText) ?? AppVersionInfo.CreateUnknown(baseVersion);
    }

    /// <summary>读取嵌入资源并构造动态版本，base 版本取程序集/manifest 版本；不抛异常。</summary>
    public static AppVersionInfo Load()
    {
        string baseVersion = "0.0.0.0";
        try
        {
            baseVersion = RuntimeHelper.GetVersion();
        }
        catch
        {
            // 读不到基础版本时用固定值兜底，动态版本不应影响启动
        }

        return LoadFrom(ReadResourceText(), baseVersion);
    }

    private static string? ReadResourceText()
    {
        try
        {
            using Stream? stream = typeof(AppVersion).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null) return null;
            using StreamReader reader = new(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }
}
