using System.ComponentModel;
using System.Reflection;
using Snap.Nicole.Core;
using Snap.Nicole.Core.IO;
using Snap.Nicole.Services.Settings;

namespace Snap.Nicole.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SettingsRecoveryTests
{
    [TestMethod]
    [DataRow("{")]
    [DataRow("null")]
    public void FailedLoadCannotOverwriteOriginal(string original)
    {
        WithDirectory(path =>
        {
            File.WriteAllText(path, original);
            using JsonSettingsProvider<TestSettings> provider = new("test", new());
            provider.CurrentValue.Value = "changed after failed load";
            Assert.AreEqual(original, File.ReadAllText(path));
        });
    }

    [TestMethod]
    public void LockedFileRemainsProtected()
    {
        WithDirectory(path =>
        {
            File.WriteAllText(path, "{}");
            using (FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (JsonSettingsProvider<TestSettings> provider = new("test", new()))
            {
                provider.CurrentValue.Value = "changed while locked";
            }
            Assert.AreEqual("{}", File.ReadAllText(path));
        });
    }

    [TestMethod]
    public void SuccessfulReloadEnablesWritesAgain()
    {
        WithDirectory(path =>
        {
            File.WriteAllText(path, "{");
            using JsonSettingsProvider<TestSettings> provider = new("test", new());
            provider.CurrentValue.Value = "ignored";
            File.WriteAllText(path, "{}");
            typeof(JsonSettingsProvider<TestSettings>).GetMethod("OnFileChanged", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [provider]);
            provider.CurrentValue.Value = "recovered";
            Assert.AreEqual("recovered", System.Text.Json.JsonSerializer.Deserialize<TestSettings>(File.ReadAllText(path))!.Value);
        });
    }

    private static void WithDirectory(Action<string> test)
    {
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SnapNicoleSettingsTests"));
        string directory = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        WellKnownLocations.Settings = directory;
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext context = new ImmediateContext();
        App.Current.Threading.SynchronizationContext = context;
        SynchronizationContext.SetSynchronizationContext(context);
        try { test(Path.Combine(directory, "test.json")); }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            if (!Path.GetFullPath(directory).StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Unexpected test directory");
            }
            Directory.Delete(directory, true);
        }
    }

    private sealed class ImmediateContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            callback(state);
        }
    }

    public sealed class TestSettings : INotifyPropertyChanged, ICopyFrom<TestSettings>
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Value
        {
            get;
            set
            {
                if (field != value)
                {
                    field = value;
                    PropertyChanged?.Invoke(this, new(nameof(Value)));
                }
            }
        } = string.Empty;

        public void CopyFrom(TestSettings source)
        {
            Value = source.Value;
        }
    }
}
