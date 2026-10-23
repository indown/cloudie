using Cloudie.Core.Settings;

namespace Cloudie.Core.Tests;

public class JsonFileSettingsStoreTests
{
    [Fact]
    public void Values_survive_a_reload_and_removal_deletes_the_key()
    {
        var path = Path.Combine(Path.GetTempPath(), "cloudie-tests", Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var store = new JsonFileSettingsStore(path);
            Assert.Null(store.GetString("a"));
            store.SetString("a", "1");
            store.SetBool("b", true);
            Assert.True(File.Exists(path));

            var reloaded = new JsonFileSettingsStore(path);
            Assert.Equal("1", reloaded.GetString("a"));
            Assert.True(reloaded.GetBool("b", false));

            reloaded.SetString("a", null);
            Assert.Null(new JsonFileSettingsStore(path).GetString("a"));
            Assert.Equal(0.8, new AppSettings(reloaded).Volume);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
