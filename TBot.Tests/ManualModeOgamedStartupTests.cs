using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Tests;

public class ManualModeOgamedStartupTests {
    [Theory]
    [InlineData(1)]
    [InlineData(120)]
    [InlineData(300)]
    [InlineData(int.MaxValue)]
    public void Startup_and_restart_pass_the_same_configured_timeout(int seconds) {
        using var service = new RecordingOgameService();
        service.Initialize(Credentials(), new Device(), new ProxySettings(), manualModeTimeoutSeconds: seconds);
        service.RerunOgamed();

        Assert.Equal(2, service.Arguments.Count);
        Assert.All(service.Arguments, args => {
            var timeout = Assert.Single(args.Split(' ').Where(arg => arg.StartsWith("--manual-mode-timeout-seconds=")));
            Assert.Equal("--manual-mode-timeout-seconds=" + seconds.ToString(CultureInfo.InvariantCulture), timeout);
        });
        Assert.Equal(service.Arguments[0], service.Arguments[1]);
    }

    [Fact]
    public void Calls_through_the_interface_without_the_new_option_keep_working_with_two_minutes() {
        using var service = new RecordingOgameService();
        IOgameService contract = service;
        contract.Initialize(Credentials(), new Device(), new ProxySettings());
        contract.RerunOgamed();

        Assert.Equal(2, service.Arguments.Count);
        Assert.All(service.Arguments, args => Assert.Contains("--manual-mode-timeout-seconds=120", args));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_programmatic_values_cannot_start_the_daemon(int seconds) {
        using var service = new RecordingOgameService();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Initialize(Credentials(), new Device(), new ProxySettings(), manualModeTimeoutSeconds: seconds));
        Assert.Empty(service.Arguments);
    }

    private static Credentials Credentials() => new() {
        Universe = "Offline", Username = "fixture@example.invalid", Password = "fixture-only",
        Language = "en", BasicAuthUsername = "", BasicAuthPassword = ""
    };

    // This override records the real production-generated arguments, but never
    // creates a subprocess, opens a socket, logs in or contacts the running bot.
    private sealed class RecordingOgameService() : OgameService(
        StrictInterfaceProxy.Make<ILoggerService<OgameService>>((method, args) => null)), IDisposable {
        public List<string> Arguments { get; } = new();
        private readonly List<Process> processes = new();

        protected override Process StartOgamedProcess(string arguments) {
            Arguments.Add(arguments);
            var process = new Process();
            processes.Add(process);
            return process;
        }

        public void Dispose() {
            var client = (HttpClient)typeof(OgameService).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(this);
            client?.Dispose();
            foreach (var process in processes)
                process.Dispose();
        }
    }
}
