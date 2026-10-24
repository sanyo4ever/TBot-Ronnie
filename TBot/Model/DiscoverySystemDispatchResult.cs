using System.Collections.Generic;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Model {
	public sealed class DiscoverySystemDispatchResult {
		public DiscoveryDispatchStatus Status { get; }
		public IReadOnlyList<Coordinate> Coordinates { get; }
		public string Message { get; }

		public bool IsConfirmed => Status == DiscoveryDispatchStatus.Sent;

		public DiscoverySystemDispatchResult(
			DiscoveryDispatchStatus status,
			IReadOnlyList<Coordinate> coordinates = null,
			string message = "") {
			Status = status;
			Coordinates = coordinates ?? new List<Coordinate>();
			Message = string.IsNullOrWhiteSpace(message) ? status.ToString() : message.Trim();
		}
	}
}
