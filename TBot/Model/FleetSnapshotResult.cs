using System.Collections.Generic;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Model {
	/// <summary>
	/// The fleet list and the result of the exact refresh that produced it.
	/// Keeping them together prevents workers from observing a freshness flag
	/// belonging to a different concurrent refresh.
	/// </summary>
	public sealed record FleetSnapshotResult(IReadOnlyList<Fleet> Fleets, bool IsFresh);
}
