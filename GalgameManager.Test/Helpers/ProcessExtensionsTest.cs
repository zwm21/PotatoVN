using System.Diagnostics;
using GalgameManager.Helpers;

namespace GalgameManager.Test.Helpers;

[TestFixture]
public class ProcessExtensionsTest
{
    [Test]
    public void TryGetExecutablePath_CurrentProcess_ReturnsExistingFile()
    {
        using Process process = Process.GetCurrentProcess();
        string? path = process.TryGetExecutablePath();

        Assert.That(path, Is.EqualTo(Environment.ProcessPath).IgnoreCase);
        Assert.That(File.Exists(path), Is.True);
    }

    [Test]
    public void TryGetExecutablePath_ExitedProcess_ReturnsNull()
    {
        using Process process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0")
        {
            CreateNoWindow = true,
        })!;
        process.WaitForExit();

        // 进程已退出，不应抛出异常
        Assert.That(process.TryGetExecutablePath(), Is.Null);
    }
}
