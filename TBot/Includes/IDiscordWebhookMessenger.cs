using System.Threading;
using System.Threading.Tasks;
using TBot.Model;

namespace Tbot.Includes {
	public interface IDiscordWebhookMessenger {
		Task SendMessage(string message, NotificationKind kind = NotificationKind.Information, CancellationToken cancellationToken = default);
	}
}
