using System.Reflection;
using Tbot.Helpers;
using Xunit;

namespace TBot.Tests;

public class VersionTitleTests {
    private static string ExpectedVersion() {
        var assembly = typeof(ConsoleHelpers).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        Assert.NotNull(informationalVersion);
        return informationalVersion.InformationalVersion.Split('+')[0];
    }

    [Fact]
    public void WindowTitleDisplaysTheAssemblyInformationalVersion() {
        Assert.Equal($"TBot v{ExpectedVersion()}", ConsoleHelpers.GetTitle());
    }

    [Fact]
    public void StatusPrefixKeepsTheInformationalVersionVisible() {
        Assert.Equal($"Checking - TBot v{ExpectedVersion()}", ConsoleHelpers.GetTitle("Checking"));
    }
}
