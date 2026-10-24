using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TBot.Model;
using Tbot.Services;
using Xunit;

namespace TBot.Tests {
	public class DiscordWebhookMessengerTests {
		[Fact]
		public async Task SendMessagePostsEmbedPayloadAndDisablesMentions() {
			var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
			using var client = new HttpClient(handler);
			var messenger = new DiscordWebhookMessenger(
				"https://discord.test/api/webhooks/id/token",
				"TBot",
				client);

			await messenger.SendMessage("<code>[Player@Universe]</code>\nReady &amp; safe");

			Assert.Single(handler.Requests);
			Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
			using var json = JsonDocument.Parse(handler.Requests[0].Body);
			var embed = Assert.Single(json.RootElement.GetProperty("embeds").EnumerateArray());
			Assert.Equal("TBot notification", embed.GetProperty("title").GetString());
			Assert.Equal("`[Player@Universe]`\nReady & safe", embed.GetProperty("description").GetString());
			Assert.Equal(5793266, embed.GetProperty("color").GetInt32());
			Assert.False(string.IsNullOrWhiteSpace(embed.GetProperty("timestamp").GetString()));
			Assert.Equal("TBot", embed.GetProperty("footer").GetProperty("text").GetString());
			Assert.Equal("TBot", json.RootElement.GetProperty("username").GetString());
			Assert.Empty(json.RootElement.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray());
		}

		[Fact]
		public async Task SendMessageUsesAttackColorAndTitle() {
			var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
			using var client = new HttpClient(handler);
			var messenger = new DiscordWebhookMessenger(
				"https://discord.test/api/webhooks/id/token",
				httpClient: client);

			await messenger.SendMessage("Incoming attack", NotificationKind.Attack);

			using var json = JsonDocument.Parse(handler.Requests[0].Body);
			var embed = Assert.Single(json.RootElement.GetProperty("embeds").EnumerateArray());
			Assert.Equal("Incoming attack", embed.GetProperty("title").GetString());
			Assert.Equal(15548997, embed.GetProperty("color").GetInt32());
		}

		[Fact]
		public async Task SendMessageSplitsMessagesAtDiscordLimit() {
			var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
			using var client = new HttpClient(handler);
			var messenger = new DiscordWebhookMessenger(
				"https://discord.test/api/webhooks/id/token",
				httpClient: client);

			await messenger.SendMessage(new string('x', 4001));

			Assert.Equal(3, handler.Requests.Count);
			Assert.All(handler.Requests, request => {
				using var json = JsonDocument.Parse(request.Body);
				var embed = Assert.Single(json.RootElement.GetProperty("embeds").EnumerateArray());
				Assert.InRange(embed.GetProperty("description").GetString().Length, 1, 2000);
			});
		}

		[Fact]
		public async Task SendMessageReportsHttpFailureWithoutLeakingWebhookUrl() {
			const string webhookUrl = "https://discord.test/api/webhooks/id/secret-token";
			var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
			using var client = new HttpClient(handler);
			var messenger = new DiscordWebhookMessenger(webhookUrl, httpClient: client);

			var exception = await Assert.ThrowsAsync<HttpRequestException>(() => messenger.SendMessage("test"));

			Assert.Contains("500", exception.Message);
			Assert.DoesNotContain(webhookUrl, exception.Message);
		}

		[Fact]
		public void ConstructorRejectsNonHttpWebhookUrls() {
			Assert.Throws<ArgumentException>(() => new DiscordWebhookMessenger("file:///tmp/webhook"));
		}

		private sealed class RecordingHandler : HttpMessageHandler {
			private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;
			public List<RecordedRequest> Requests { get; } = new();

			public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) {
				_responseFactory = responseFactory;
			}

			protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
				Requests.Add(new RecordedRequest(
					request.Method,
					request.RequestUri,
					await request.Content.ReadAsStringAsync(cancellationToken)));
				return _responseFactory(request);
			}
		}

		private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body);
	}
}
