using System;

namespace TBot.Model {
	public enum DiscoveryDispatchStatus {
		Sent,
		NoSlots,
		NoShips,
		PositionUnavailable,
		RequestPending,
		SessionExpired,
		Rejected,
		Failed
	}

	/// <summary>
	/// Describes the outcome of one discovery dispatch without reducing every
	/// non-successful response to the same generic error.
	/// </summary>
	public sealed class DiscoveryDispatchResult {
		public DiscoveryDispatchStatus Status { get; }
		public string Message { get; }

		public bool IsConfirmed => Status == DiscoveryDispatchStatus.Sent;
		public bool CanContinueWithAnotherPosition =>
			Status == DiscoveryDispatchStatus.PositionUnavailable;

		public DiscoveryDispatchResult(DiscoveryDispatchStatus status, string message = "") {
			Status = status;
			Message = string.IsNullOrWhiteSpace(message) ? status.ToString() : message.Trim();
		}
	}
}
