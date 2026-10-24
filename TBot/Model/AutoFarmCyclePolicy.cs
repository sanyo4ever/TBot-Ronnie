namespace TBot.Model {
	/// <summary>
	/// Decides when a completed scan may be committed as the next cycle.
	/// A report-list failure must leave the scan completion uncommitted so the
	/// next execution can finish the pending work instead of starting over.
	/// </summary>
	public static class AutoFarmCyclePolicy {
		public static bool CanFinalizeFullScan(bool fullScanCompleted, bool reportPhaseCompleted) {
			return fullScanCompleted && reportPhaseCompleted;
		}
	}
}
