using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TBot.Common.Logging;
using Tbot.Includes;
using Tbot.Services;
using Tbot.Workers;
using Tbot.Common.Settings;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Models;
using TBot.Model;
using Xunit;

namespace TBot.Tests {
	public class NotificationRoutingTests {
		[Fact]
		public async Task DefenderNotificationUsesDiscordWhenTelegramIsDisabled() {
			var settings = CreateSettings("true");
			var discord = new RecordingDiscordMessenger();
			var main = CreateMain(settings, discord);

			await main.SendFeatureNotification(settings.Defender, "incoming attack");

			var message = Assert.Single(discord.Messages);
			Assert.Contains("incoming attack", message);
		}

		[Fact]
		public async Task FeatureDiscordSwitchCanDisableOnlyThatFeature() {
			var settings = CreateSettings("false");
			var discord = new RecordingDiscordMessenger();
			var main = CreateMain(settings, discord);

			await main.SendFeatureNotification(settings.Defender, "incoming attack");

			Assert.Empty(discord.Messages);
		}

		[Fact]
		public async Task FeatureNotificationPassesItsKindToDiscord() {
			var settings = CreateSettings("true");
			var discord = new RecordingDiscordMessenger();
			var main = CreateMain(settings, discord);

			await main.SendFeatureNotification(settings.Defender, "incoming attack", NotificationKind.Attack);

			Assert.Equal(NotificationKind.Attack, Assert.Single(discord.Kinds));
		}

		[Fact]
		public void GlobalDiscordSwitchControlsWebhookInitialization() {
			var disabledSettings = CreateSettings("true", false);
			var disabledMain = CreateMain(disabledSettings, new RecordingDiscordMessenger());
			SetInstanceSettings(disabledMain, disabledSettings);
			InvokeConfigureDiscordWebhook(disabledMain);

			var messengerField = typeof(TBotMain).GetField(
				"discordWebhookMessenger",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.NotNull(messengerField);
			Assert.Null(messengerField.GetValue(disabledMain));

			var enabledSettings = CreateSettings("true", true);
			var enabledMain = CreateMain(enabledSettings, new RecordingDiscordMessenger());
			SetInstanceSettings(enabledMain, enabledSettings);
			InvokeConfigureDiscordWebhook(enabledMain);
			Assert.NotNull(messengerField.GetValue(enabledMain));
		}

		private static dynamic CreateSettings(string defenderDiscordActive, bool globalDiscordActive = true) {
			return SettingsService.GetMergedSettingsFromContents(
				$$"""
{
  "DiscordWebhook": { "Active": {{globalDiscordActive.ToString().ToLowerInvariant()}}, "Url": "https://discord.test/webhook" },
  "Defender": {
    "TelegramMessenger": { "Active": false },
    "DiscordWebhook": { "Active": {{defenderDiscordActive}} }
  }
}
""",
				new List<string> { "{}" });
		}

		private static void SetInstanceSettings(TBotMain main, dynamic settings) {
			var property = typeof(TBotMain).GetProperty("InstanceSettings");
			Assert.NotNull(property);
			property.SetValue(main, settings);
		}

		private static void InvokeConfigureDiscordWebhook(TBotMain main) {
			var method = typeof(TBotMain).GetMethod(
				"ConfigureDiscordWebhook",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.NotNull(method);
			method.Invoke(main, null);
		}

		private static TBotMain CreateMain(dynamic settings, IDiscordWebhookMessenger discord) {
			var main = new TBotMain(
				Proxy<IOgameService>.Create(),
				Proxy<IFleetScheduler>.Create(),
				Proxy<ICalculationService>.Create(),
				Proxy<IWorkerFactory>.Create(),
				Proxy<ILoggerService<TBotMain>>.Create());

			var instanceSettings = typeof(TBotMain).GetField(
				"discordWebhookMessenger",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.NotNull(instanceSettings);
			instanceSettings.SetValue(main, discord);
			return main;
		}

		private sealed class RecordingDiscordMessenger : IDiscordWebhookMessenger {
			public List<string> Messages { get; } = new();
			public List<NotificationKind> Kinds { get; } = new();

			public Task SendMessage(string message, NotificationKind kind = NotificationKind.Information, CancellationToken cancellationToken = default) {
				Messages.Add(message);
				Kinds.Add(kind);
				return Task.CompletedTask;
			}
		}

		private class Proxy<T> : DispatchProxy where T : class {
			public static T Create() {
				return DispatchProxy.Create<T, Proxy<T>>();
			}

			protected override object Invoke(MethodInfo targetMethod, object[] args) {
				if (targetMethod.ReturnType == typeof(Task))
					return Task.CompletedTask;
				if (targetMethod.ReturnType.IsGenericType &&
					targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)) {
					var resultType = targetMethod.ReturnType.GetGenericArguments()[0];
					return typeof(Task).GetMethod(nameof(Task.FromResult))
						.MakeGenericMethod(resultType)
						.Invoke(null, new[] { DefaultValue(resultType) });
				}

				return DefaultValue(targetMethod.ReturnType);
			}

			private static object DefaultValue(Type type) {
				if (type == typeof(void))
					return null;
				return type.IsValueType ? Activator.CreateInstance(type) : null;
			}
		}
	}
}
