using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GalgameManager.Helpers;

/// <summary>
/// 动态版本信息：构建期由 git 采集后写入嵌入资源，运行期只读该资源。<br/>
/// 采集与解析均不使用时间戳、随机量，因此同一份源码在任何机器上得到的版本一致。<br/>
/// 资源行格式：<c>base|commits|commit|[delta]</c>
/// <list type="bullet">
///   <item>base：Package.appxmanifest 的 Identity Version，如 1.10.2.1</item>
///   <item>commits：git 提交总数，无 git 时为 0</item>
///   <item>commit：7 位提交 hash，无 git 时为空</item>
///   <item>delta：工作区未提交改动（含未跟踪，排除 ignore 与 Build/）的 8 位 SHA256，干净时为空</item>
/// </list>
/// </summary>
public sealed class AppVersionInfo
{
    /// <summary>资源行字段分隔符</summary>
    public const char Separator = '|';

    /// <summary>资源缺失或损坏时的日志行后缀</summary>
    private const string UnknownSource = "src.unknown";

    /// <summary>delta 的位数：SHA256 十六进制串的前 8 位</summary>
    public const int Sha256PrefixLength = 8;

    /// <summary>基础版本号（Package.appxmanifest 的 Identity Version）</summary>
    public string BaseVersion { get; }

    /// <summary>git 提交总数，无 git 时为 0</summary>
    public int CommitCount { get; }

    /// <summary>7 位提交 hash，无 git 时为 null</summary>
    public string? CommitHash { get; }

    /// <summary>工作区未提交改动的 8 位 SHA256，干净或未采集到时为 null</summary>
    public string? Delta { get; }

    /// <summary>是否采集到了 git 信息</summary>
    public bool HasGit => !string.IsNullOrEmpty(CommitHash);

    private readonly bool _unknown;

    private AppVersionInfo(string baseVersion, int commitCount, string? commitHash, string? delta, bool unknown)
    {
        BaseVersion = baseVersion;
        CommitCount = commitCount;
        CommitHash = string.IsNullOrEmpty(commitHash) ? null : commitHash;
        Delta = string.IsNullOrEmpty(delta) ? null : delta;
        _unknown = unknown;
    }

    /// <summary>资源缺失或损坏时的回退实例</summary>
    public static AppVersionInfo CreateUnknown(string baseVersion)
    {
        return new AppVersionInfo(baseVersion, 0, null, null, true);
    }

    /// <summary>解析资源行；格式非法或字段自相矛盾时返回 null（调用方据此回退）</summary>
    public static AppVersionInfo? TryParse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        string[] fields = line.Trim().Split(Separator);
        if (fields.Length is < 3 or > 4) return null;

        string baseVersion = fields[0].Trim();
        if (baseVersion.Length == 0) return null;
        if (!int.TryParse(fields[1].Trim(), out int commits) || commits < 0) return null;

        string commit = fields[2].Trim();
        string delta = fields.Length == 4 ? fields[3].Trim() : string.Empty;
        if (delta.Length > 0 && (delta.Length != Sha256PrefixLength || !IsHex(delta))) return null;

        if (commit.Length == 0)
            // 无 git：不允许同时声明提交数
            return commits == 0 ? new AppVersionInfo(baseVersion, 0, null, null, false) : null;
        if (commits == 0 || commit.Length is < 4 or > 40 || !IsHex(commit)) return null;

        return new AppVersionInfo(baseVersion, commits, commit, delta.Length == 0 ? null : delta, false);
    }

    /// <summary>序列化为资源行</summary>
    public string ToResourceLine()
    {
        return string.Join(Separator, BaseVersion, CommitCount, CommitHash ?? string.Empty, Delta ?? string.Empty);
    }

    /// <summary>
    /// 日志行：干净 <c>1.10.2.1+r1234.g3f91c2</c>；脏 <c>1.10.2.1+r1234.g3f91c2+4a1b2c3d</c>；
    /// 无 git <c>1.10.2.1+src.&lt;指纹&gt;</c>；资源缺失/损坏 <c>1.10.2.1+src.unknown</c>
    /// </summary>
    public string ToLogLine()
    {
        if (_unknown) return $"{BaseVersion}+{UnknownSource}";
        if (!HasGit) return $"{BaseVersion}+src.{ComputeSourceFingerprint(BaseVersion)}";
        string delta = Delta is null ? string.Empty : $"+{Delta}";
        return $"{BaseVersion}+r{CommitCount}.g{CommitHash}{delta}";
    }

    /// <summary>UI 显示的版本串：<c>v1.10.1234</c>；无 git 时末位取指纹哈希值，资源缺失时取固定值 0</summary>
    public string ToDisplayVersion()
    {
        int number = _unknown ? 0 : HasGit ? CommitCount : FingerprintNumber;
        if (!Version.TryParse(BaseVersion, out Version? version) || version is null) return $"v{BaseVersion}";
        return $"v{version.Major}.{version.Minor}.{number}";
    }

    /// <summary>无 git 时的源码指纹：base 版本的 SHA256 前 8 位十六进制（确定性，与构建时间无关）</summary>
    public static string ComputeSourceFingerprint(string baseVersion)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"src:{baseVersion}"));
        return Convert.ToHexString(hash)[..Sha256PrefixLength].ToLowerInvariant();
    }

    private int FingerprintNumber
    {
        get
        {
            string fingerprint = ComputeSourceFingerprint(BaseVersion);
            return int.Parse(fingerprint[..4], NumberStyles.HexNumber);
        }
    }

    private static bool IsHex(string value)
    {
        if (value.Length == 0) return false;
        foreach (char c in value)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }
}
