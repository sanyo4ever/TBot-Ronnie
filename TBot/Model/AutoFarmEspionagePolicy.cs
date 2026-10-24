	using System;
	using System.Collections.Generic;
using System.Linq;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Model {
	public static class AutoFarmEspionagePolicy {
		public static bool HasOwnedMissionTo(
			IEnumerable<Fleet> fleets,
			Coordinate destination,
			ISet<int> ownedFleetIds) {
			if (destination == null)
				return false;

			return (fleets ?? Enumerable.Empty<Fleet>())
				.Any(fleet => fleet?.Mission == Missions.Spy &&
					ownedFleetIds?.Contains(fleet.ID) == true &&
					fleet.Destination?.IsSame(destination) == true);
		}

		public static IReadOnlyList<Fleet> GetMissionsToAwait(
			IEnumerable<Fleet> fleets,
			ISet<int> ownedFleetIds) {
			return (fleets ?? Enumerable.Empty<Fleet>())
				.Where(fleet => fleet?.Mission == Missions.Spy
					&& ownedFleetIds?.Contains(fleet.ID) == true)
				.ToList();
		}

		public static bool ShouldReopenMissingProbe(
			DateTime? probeSentAtUtc,
			DateTime nowUtc,
			TimeSpan gracePeriod) {
			if (!probeSentAtUtc.HasValue)
				return true;

			var age = nowUtc.ToUniversalTime() - probeSentAtUtc.Value.ToUniversalTime();
			return age >= gracePeriod;
		}
	}
}
