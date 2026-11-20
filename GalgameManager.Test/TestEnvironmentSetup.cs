namespace GalgameManager.Test;

[SetUpFixture]
[NonParallelizable]
public sealed class TestEnvironmentSetup
{
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "PotatoVN.Test", Guid.NewGuid().ToString("N"));
    public static readonly string LocalDataPath = Path.Combine(Root, "LocalData");
    private string? _previousLocalDataPath;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _previousLocalDataPath = Environment.GetEnvironmentVariable("POTATOVN_LOCALDATA_PATH");
        Environment.SetEnvironmentVariable("POTATOVN_LOCALDATA_PATH", LocalDataPath);
        Directory.CreateDirectory(LocalDataPath);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("POTATOVN_LOCALDATA_PATH", _previousLocalDataPath);
        }
    }
}
