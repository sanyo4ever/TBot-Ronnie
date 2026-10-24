using System;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Model {
	/// <summary>
	/// Defines how a persisted farm target is reopened after fresh galaxy data
	/// has been observed. In-flight work and pending attacks must survive a
	/// system refresh; old decisions based on an espionage report must not be
	/// used to block a new probe. The previous report is retained as an
	/// identity marker so a report that could not be deleted is never processed
	/// as a new report.
	/// </summary>
	public static class AutoFarmTargetPolicy {
		/// <summary>
		/// A recent NotSuitable report is a cooldown, not a permanent blacklist.
		/// Once it expires the caller can move the target back to ProbesPending.
		/// </summary>
		public static bool IsWaitingForReportCooldown(
			FarmTarget target,
			DateTime nowUtc,
			TimeSpan reportCooldown,
			out DateTime nextProbeAtUtc) {
			nextProbeAtUtc = DateTime.MinValue;
			if (target?.Report == null || target.State != FarmState.NotSuitable || reportCooldown <= TimeSpan.Zero)
				return false;

			var reportDateUtc = target.Report.Date.ToUniversalTime();
			nextProbeAtUtc = reportDateUtc.Add(reportCooldown);
			return nowUtc < nextProbeAtUtc;
		}

		public static bool ResetForFreshSystemData(FarmTarget target) {
			if (target == null || !NeedsNewProbe(target.State))
				return false;

			target.State = FarmState.ProbesPending;
			target.ProbeFleetId = null;
			target.ProbeSentAtUtc = null;
			return true;
		}

		/// <summary>
		/// A refreshed galaxy row does not make a recent espionage report stale.
		/// The report retention setting remains the single source of truth for
		/// deciding when a target may be probed again.
		/// </summary>
		public static bool ShouldReopenAfterSystemRefresh(
			FarmTarget target,
			DateTime nowUtc,
			TimeSpan reportCooldown) {
			if (target == null || !NeedsNewProbe(target.State))
				return false;

			return target.State != FarmState.NotSuitable ||
				!IsWaitingForReportCooldown(target, nowUtc, reportCooldown, out _);
		}

		private static bool NeedsNewProbe(FarmState state) {
			return state == FarmState.Idle
				|| state == FarmState.NotSuitable
				|| state == FarmState.ProbesRequired
				|| state == FarmState.FailedProbesRequired;
		}
	}
}
