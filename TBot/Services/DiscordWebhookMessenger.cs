using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TBot.Model;
using Tbot.Includes;

namespace Tbot.Services {
	/// <summary>
	/// Sends TBot notifications to a Discord incoming webhook.
	/// Discord webhooks are outbound-only: they cannot receive bot commands.
	/// </summary>
	public sealed class DiscordWebhookMessenger : IDiscordWebhookMessenger {
		public const int DiscordMessageLimit = 2000;

		private static readonly HttpClient SharedHttpClient = new HttpClient {
			Timeout = TimeSpan.FromSeconds(10)
		};

		private readonly Uri _webhookUri;
		private readonly string _username;
		private readonly HttpClient _httpClient;

		public DiscordWebhookMessenger(string webhookUrl, string username = null, HttpClient httpClient = null) {
			if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri) ||
				(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
				throw new ArgumentException("Discord webhook URL must be an absolute HTTP or HTTPS URL.", nameof(webhookUrl));

			_webhookUri = uri;
			_username = username?.Trim();
			_httpClient = httpClient ?? SharedHttpClient;
		}

		public async Task SendMessage(string message, NotificationKind kind = NotificationKind.Information, CancellationToken cancellationToken = default) {
			var formattedMessage = FormatMessage(message);
			if (string.IsNullOrWhiteSpace(formattedMessage))
				return;

			foreach (var chunk in SplitMessage(formattedMessage)) {
				var embed = new Dictionary<string, object> {
					["title"] = GetTitle(kind),
					["description"] = chunk,
					["color"] = GetColor(kind),
					["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
					["footer"] = new Dictionary<string, object> {
						["text"] = "TBot"
					}
				};
				var payload = new Dictionary<string, object> {
					["embeds"] = new[] { embed },
					// TBot messages can contain player names or coordinates. Never let
					// notification text trigger an accidental Discord mention.
					["allowed_mentions"] = new Dictionary<string, object> {
						["parse"] = Array.Empty<string>()
					}
				};
				if (!string.IsNullOrWhiteSpace(_username))
					payload["username"] = _username;

				using var request = new HttpRequestMessage(HttpMethod.Post, _webhookUri) {
					Content = new StringContent(
						JsonConvert.SerializeObject(payload),
						Encoding.UTF8,
						"application/json")
				};
				using var response = await _httpClient.SendAsync(
					request,
					HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);

				if (!response.IsSuccessStatusCode)
					throw new HttpRequestException(
						$"Discord webhook returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
			}
		}

		private static string GetTitle(NotificationKind kind) {
			return kind switch {
				NotificationKind.Attack => "Incoming attack",
				NotificationKind.MissileAttack => "Incoming missile attack",
				NotificationKind.Warning => "Warning",
				NotificationKind.Success => "Action completed",
				NotificationKind.Error => "Error",
				NotificationKind.SleepMode => "Sleep mode",
				_ => "TBot notification"
			};
		}

		private static int GetColor(NotificationKind kind) {
			return kind switch {
				NotificationKind.Attack => 15548997,        // #ED4245
				NotificationKind.MissileAttack => 15105570, // #E67E22
				NotificationKind.Warning => 15105570,       // #E67E22
				NotificationKind.Success => 5763719,        // #57F287
				NotificationKind.Error => 10038562,         // #992D22
				NotificationKind.SleepMode => 5998254,      // #5B6EAE
				_ => 5793266                             // #5865F2
			};
		}

		private static string FormatMessage(string message) {
			if (string.IsNullOrWhiteSpace(message))
				return string.Empty;

			return WebUtility.HtmlDecode(message)
				.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
				.Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
				.Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
				.Replace("<code>", "`", StringComparison.OrdinalIgnoreCase)
				.Replace("</code>", "`", StringComparison.OrdinalIgnoreCase)
				.Replace("<b>", "**", StringComparison.OrdinalIgnoreCase)
				.Replace("</b>", "**", StringComparison.OrdinalIgnoreCase)
				.Trim();
		}

		private static IEnumerable<string> SplitMessage(string message) {
			var offset = 0;
			while (offset < message.Length) {
				var length = Math.Min(DiscordMessageLimit, message.Length - offset);
				if (offset + length < message.Length) {
					var newline = message.LastIndexOf('\n', offset + length - 1, length);
					if (newline > offset)
						length = newline - offset + 1;
				}

				// Do not split a UTF-16 surrogate pair between two Discord messages.
				if (offset + length < message.Length &&
					length > 0 && char.IsHighSurrogate(message[offset + length - 1]))
					length--;

				if (length <= 0)
					length = Math.Min(DiscordMessageLimit, message.Length - offset);

				yield return message.Substring(offset, length);
				offset += length;
			}
		}
	}
}
