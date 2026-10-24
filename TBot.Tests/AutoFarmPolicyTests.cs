using TBot.Model;
using TBot.Ogame.Infrastructure.Models;
using Xunit;

namespace TBot.Tests {
	public class AutoFarmPolicyTests {
		[Fact]
		public void MissilesOnlyAreIgnoredWhenConfigured() {
			var report = new EspionageReport {
				Metal = 1000000,
				HasFleetInformation = true,
				HasDefensesInformation = true,
				AntiBallisticMissiles = 100
			};

			var decision = AutoFarmReportPolicy.Evaluate(
				report, 0, "", default, false, false, true, 0, 0);

			Assert.Equal(AutoFarmReportDecisionKind.AttackPending, decision.Kind);
			Assert.False(AutoFarmReportPolicy.ShouldPreserveForManualReview(report, true, true));
			Assert.True(AutoFarmReportPolicy.ShouldPreserveForManualReview(report, true, false));
		}

		[Fact]
		public void DefenseAndFleetLimitsAreAppliedInOgamePoints() {
			var report = new EspionageReport {
				Metal = 1000000,
				HasFleetInformation = true,
				HasDefensesInformation = true,
				RocketLauncher = 10,
				Reaper = 1
			};

			var decision = AutoFarmReportPolicy.Evaluate(
				report, 0, "", default, true, true, true,
				report.GetDefensePoints(false), report.GetFleetPoints());

			Assert.Equal(AutoFarmReportDecisionKind.AttackPending, decision.Kind);

			var defenseTooLarge = AutoFarmReportPolicy.Evaluate(
				report, 0, "", default, true, true, true,
				report.GetDefensePoints(false) - 1, 0);
			Assert.Equal(AutoFarmReportDecisionKind.NotSuitable, defenseTooLarge.Kind);
			Assert.Equal("DefenseLimitExceeded", defenseTooLarge.Reason);

			var fleetTooLarge = AutoFarmReportPolicy.Evaluate(
				report, 0, "", default, true, true, true,
				0, report.GetFleetPoints() - 1);
			Assert.Equal(AutoFarmReportDecisionKind.NotSuitable, fleetTooLarge.Kind);
			Assert.Equal("FleetLimitExceeded", fleetTooLarge.Reason);
		}

		[Fact]
		public void IncompleteReportsRequestMoreInformationBeforeCombatDecision() {
			var report = new EspionageReport {
				Metal = 1000000,
				HasFleetInformation = true,
				HasDefensesInformation = false
			};

			var decision = AutoFarmReportPolicy.Evaluate(
				report, 0, "", default, true, true, true, 0, 0);

			Assert.Equal(AutoFarmReportDecisionKind.NeedMoreInformation, decision.Kind);
		}
	}
}
