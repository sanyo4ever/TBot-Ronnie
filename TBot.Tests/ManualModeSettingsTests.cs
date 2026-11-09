using System.Reflection;
using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure;

namespace TBot.Tests;

public class ManualModeSettingsTests {
    [Theory]
    [InlineData("{}", 120)]
    [InlineData("{\"ManualModeTimeoutSeconds\":1}", 1)]
    [InlineData("{\"ManualModeTimeoutSeconds\":120}", 120)]
    [InlineData("{\"ManualModeTimeoutSeconds\":300}", 300)]
    [InlineData("{\"ManualModeTimeoutSeconds\":2147483647}", int.MaxValue)]
    public async Task Loaded_settings_accept_positive_integer_seconds_and_default_missing_values(string general, int expected) {
        dynamic settings = await LoadSettings("{\"General\":" + general + "}");
        Assert.Equal(expected, ManualModeSettings.ReadTimeoutSeconds((object)settings.General));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("120.0")]
    [InlineData("2147483648")]
    [InlineData("9223372036854775808")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("\"120\"")]
    [InlineData("\"private-setting-value\"")]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task Invalid_explicit_values_fail_without_echoing_the_value(string value) {
        dynamic settings = await LoadSettings("{\"General\":{\"ManualModeTimeoutSeconds\":" + value + "}}");
        var error = Assert.Throws<ArgumentException>(() => ManualModeSettings.ReadTimeoutSeconds((object)settings.General));
        Assert.Contains("General.ManualModeTimeoutSeconds", error.Message);
        Assert.Contains("positive whole number", error.Message);
        Assert.DoesNotContain("private-setting-value", error.Message);
    }

    [Theory]
    [InlineData(null, 120)]
    [InlineData(300, 300)]
    public async Task Instance_start_passes_the_loaded_timeout_to_ogamed(int? configured, int expected) {
        var settings = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "instance_settings.json")));
        settings["Credentials"]["Universe"] = "Offline";
        settings["Credentials"]["Language"] = "en";
        settings["General"]["ManualModeTimeoutSeconds"] = configured.HasValue ? JToken.FromObject(configured.Value) : null;
        if (!configured.HasValue)
            ((JObject)settings["General"]).Remove("ManualModeTimeoutSeconds");
        object[] initializationArguments = null;
        var service = StrictInterfaceProxy.Make<IOgameService>((method, args) => {
            if (method.Name.StartsWith("add_")) return null;
            if (method.Name == "IsPortAvailable") return true;
            if (method.Name == "Initialize") {
                initializationArguments = args;
                throw new InitializationCapturedException();
            }
            throw new NotSupportedException(method.Name);
        });
        var instance = NewInstance(service);
        typeof(TBotMain).GetProperty(nameof(TBotMain.InstanceSettings)).SetValue(instance, await LoadSettings(settings.ToString()));

        await Assert.ThrowsAsync<InitializationCapturedException>(() => Initialize(instance));

        Assert.NotNull(initializationArguments);
        Assert.Equal(7, initializationArguments.Length);
        Assert.Equal(expected, initializationArguments[6]);
    }

    [Fact]
    public async Task Invalid_timeout_is_rejected_before_port_checks_or_starting_ogamed() {
        var serviceCalls = new List<string>();
        var service = StrictInterfaceProxy.Make<IOgameService>((method, args) => {
            if (method.Name.StartsWith("add_")) return null;
            serviceCalls.Add(method.Name);
            throw new InvalidOperationException("No service call expected for invalid configuration.");
        });
        var instance = NewInstance(service);
        typeof(TBotMain).GetProperty(nameof(TBotMain.InstanceSettings)).SetValue(instance,
            await LoadSettings("{\"General\":{\"ManualModeTimeoutSeconds\":0}}"));

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Initialize(instance));

        Assert.Contains("General.ManualModeTimeoutSeconds", error.Message);
        Assert.Empty(serviceCalls);
    }

    private static TBotMain NewInstance(IOgameService service) => new(service,
        StrictInterfaceProxy.Make<IFleetScheduler>((method, args) => null), null, null,
        StrictInterfaceProxy.Make<ILoggerService<TBotMain>>((method, args) => null));

    private static Task Initialize(TBotMain instance) => (Task)typeof(TBotMain)
        .GetMethod("InitializeOgame", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);

    private static async Task<object> LoadSettings(string json) {
        var directory = Directory.CreateTempSubdirectory("tbot-manual-settings-");
        try {
            var path = Path.Combine(directory.FullName, "instance.json");
            await File.WriteAllTextAsync(path, json);
            return await SettingsService.GetSettings(path);
        } finally {
            directory.Delete(true);
        }
    }

    private sealed class InitializationCapturedException : Exception { }
}
