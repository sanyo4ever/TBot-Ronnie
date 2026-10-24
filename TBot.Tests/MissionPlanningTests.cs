using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using Microsoft.Data.Sqlite;
using Tbot.Common.Settings;
using TBot.Model;
using Tbot.Includes;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using TBot.WebUI.Services;
using Tbot.Workers;
using Xunit;

namespace TBot.Tests {
	public class MissionPlanningTests {
		[Fact]
		public void DiscoveryCursorMovesToNextSystemAfterAllFifteenPositionsAreExamined() {
			var cursor = new DiscoveryCursorState(42);

			for (var position = 1; position <= 15; position++)
				cursor.CommitPosition(42, position, 100);

			Assert.Equal(43, cursor.System);
			Assert.Equal(1, cursor.NextPosition);
		}

		[Fact]
		public void DiscoveryCursorAdvancesWhenAStartingPositionIsSkipped() {
			var cursor = new DiscoveryCursorState(42, 1);

			cursor.CommitPosition(42, 1, 100);

			Assert.Equal(42, cursor.System);
			Assert.Equal(2, cursor.NextPosition);
		}

		[Fact]
		public void AutoFarmWaitsOnlyForItsOwnEspionageMissions() {
			var fleets = new[] {
				new Fleet { ID = 100, Mission = Missions.Spy, BackIn = 20 },
				new Fleet { ID = 200, Mission = Missions.Spy, BackIn = 600 }
			};

			var missions = AutoFarmEspionagePolicy.GetMissionsToAwait(
				fleets,
				new HashSet<int> { 100 });

			Assert.Single(missions);
			Assert.Equal(100, missions[0].ID);
		}

		[Fact]
		public void AutoFarmDoesNotClaimAnUnownedSpyMissionAsItsOwn() {
			var destination = new Coordinate(1, 139, 7, Celestials.Planet);
			var fleets = new[] {
				new Fleet {
					ID = 501,
					Mission = Missions.Spy,
					Origin = new Coordinate(1, 30, 8, Celestials.Planet),
					Destination = destination
				},
				new Fleet {
					ID = 502,
					Mission = Missions.Attack,
					Destination = new Coordinate(1, 140, 7, Celestials.Planet)
				}
			};

			Assert.False(AutoFarmEspionagePolicy.HasOwnedMissionTo(
				fleets,
				destination,
				new HashSet<int> { 502 }));
			Assert.True(AutoFarmEspionagePolicy.HasOwnedMissionTo(
				fleets,
				destination,
				new HashSet<int> { 501 }));
			Assert.False(AutoFarmEspionagePolicy.HasOwnedMissionTo(
				fleets,
				new Coordinate(1, 140, 7, Celestials.Planet),
				new HashSet<int> { 501 }));
		}

		[Theory]
		[InlineData(true)]
		[InlineData(false)]
		public void AutoFarmStopsAtAHandledReportEvenWhenDeletionFails(bool deletionSucceeded) {
			// The first already-known report is the read boundary. Deletion is only
			// cleanup; its failure must not make AutoFarm walk through older reports.
			Assert.True(AutoFarmReportPolicy.ShouldStopAfterKnownReport(deletionSucceeded));
		}

		[Theory]
		[InlineData(false, false)]
		[InlineData(true, false)]
		public void AutoFarmStopsAtTheFirstUnscannedReportUnlessAllReportsAreEnabled(
			bool processAllReports,
			bool expectedResult) {
			Assert.Equal(
				expectedResult,
				AutoFarmReportPolicy.ShouldStopAfterUnscannedReport(processAllReports));
		}

		[Fact]
		public void AutoFarmStopsReportTraversalWhenAProcessedReportCannotBeDeleted() {
			// A failed delete leaves the report at the current read boundary. The
			// worker must retry it on the next execution instead of walking into
			// older reports and potentially processing the same batch again.
			Assert.True(AutoFarmReportPolicy.ShouldStopAfterDeletionFailure(false));
			Assert.False(AutoFarmReportPolicy.ShouldStopAfterDeletionFailure(true));
		}

		[Theory]
		[InlineData(true, true, true)]
		[InlineData(true, false, false)]
		[InlineData(false, true, false)]
		[InlineData(false, false, false)]
		public void AutoFarmKeepsTheReportPhasePendingWhenAnyReportDeletionFails(
			bool allReportsProcessed,
			bool deletionSucceeded,
			bool expectedResult) {
			Assert.Equal(
				expectedResult,
				AutoFarmReportPolicy.CombineProcessingResult(allReportsProcessed, deletionSucceeded));
		}

		[Fact]
		public void AutoFarmRecognizesTransientReportDeletionFailures() {
			Assert.True(AutoFarmReportPolicy.IsTransientDeletionFailure(
				new HttpRequestException("service unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable)));
			Assert.True(AutoFarmReportPolicy.IsTransientDeletionFailure(
				new InvalidOperationException("server returned HTTP status 503 Service Unavailable")));
			Assert.False(AutoFarmReportPolicy.IsTransientDeletionFailure(
				new InvalidOperationException("invalid message id")));
		}

		[Fact]
		public void AutoFarmDoesNotExpireAReportWhenKeepReportForIsZero() {
			var reportDate = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);

			Assert.False(AutoFarmReportPolicy.IsExpired(
				reportDate,
				reportDate.AddMinutes(1),
				TimeSpan.Zero));
		}

		[Fact]
		public void AutoFarmTreatsV13ZeroValuedShipsAndDefencesAsAbsent() {
			var report = new EspionageReport {
				HasFleetInformation = true,
				HasDefensesInformation = true,
				LightFighter = 0,
				RocketLauncher = 0
			};

			Assert.True(report.IsDefenceless());
		}

		[Fact]
		public void AutoFarmStillRejectsAReportWithARealShipOrDefence() {
			var report = new EspionageReport {
				HasFleetInformation = true,
				HasDefensesInformation = true,
				LightFighter = 1
			};

			Assert.False(report.IsDefenceless());
		}

		[Fact]
		public void AutoFarmPreservesOnlyReportsWithKnownDefensiveStructures() {
			var report = new EspionageReport {
				HasDefensesInformation = true,
				RocketLauncher = 1
			};

			Assert.True(report.HasDefensiveStructures());
			Assert.True(AutoFarmReportPolicy.ShouldPreserveForManualReview(report, true));
			Assert.False(AutoFarmReportPolicy.ShouldPreserveForManualReview(report, false));
		}

		[Fact]
		public void AutoFarmDoesNotPreserveEmptyOrFleetOnlyDefenceSections() {
			var emptyDefences = new EspionageReport {
				HasDefensesInformation = true,
				RocketLauncher = 0,
				PlasmaTurret = 0
			};
			var fleetOnly = new EspionageReport {
				HasFleetInformation = true,
				HasDefensesInformation = true,
				LightFighter = 1
			};

			Assert.False(emptyDefences.HasDefensiveStructures());
			Assert.False(AutoFarmReportPolicy.ShouldPreserveForManualReview(emptyDefences, true));
			Assert.False(fleetOnly.HasDefensiveStructures());
			Assert.False(AutoFarmReportPolicy.ShouldPreserveForManualReview(fleetOnly, true));
		}

		[Fact]
		public void AutoFarmExpiresReportsOnlyAfterTheConfiguredUtcRetention() {
			var reportDate = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);

			Assert.False(AutoFarmReportPolicy.IsExpired(
				reportDate,
				reportDate.AddHours(24),
				TimeSpan.FromHours(24)));
			Assert.True(AutoFarmReportPolicy.IsExpired(
				reportDate,
				reportDate.AddHours(24).AddSeconds(1),
				TimeSpan.FromHours(24)));
		}

		[Fact]
		public void DiscoveryCursorWrapsAfterTheLastSystem() {
			var cursor = new DiscoveryCursorState(100, 15);

			cursor.CommitPosition(100, 15, 100);

			Assert.Equal(1, cursor.System);
			Assert.Equal(1, cursor.NextPosition);
		}

		[Fact]
		public void DiscoverySelectsTheNearestOriginSystemPairBeforeRoundRobinTieBreak() {
			var pair = DiscoveryOriginSystemSelectionPolicy.SelectClosest(
				new[] { (1, 100), (1, 400) },
				new[] { (1, 95), (1, 390) },
				499,
				roundRobinStartIndex: 1);

			Assert.NotNull(pair);
			Assert.Equal(0, pair.OriginIndex);
			Assert.Equal(95, pair.System);
			Assert.Equal(5, pair.Distance);
		}

		[Fact]
		public void DiscoveryUsesRoundRobinOnlyWhenOriginSystemDistanceIsEqual() {
			var pair = DiscoveryOriginSystemSelectionPolicy.SelectClosest(
				new[] { (1, 100), (1, 200) },
				new[] { (1, 150) },
				499,
				roundRobinStartIndex: 1);

			Assert.NotNull(pair);
			Assert.Equal(1, pair.OriginIndex);
			Assert.Equal(50, pair.Distance);
		}

		[Fact]
		public void DiscoverySystemCacheRemovesConsumedPositionsAndInvalidatesARejectedSystem() {
			var alias = $"discovery-cache-{Guid.NewGuid():N}";
			var cachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", $"autodiscovery_systems_{alias}.json");
			try {
				var cache = new DiscoverySystemCacheStore(alias);
				cache.Observe(1, 95, new[] { 1, 2, 3 }, DateTime.UtcNow);
				cache.Consume(1, 95, new[] { 1, 2 });

				var remaining = Assert.Single(cache.LoadAvailable());
				Assert.Equal(new[] { 3 }, remaining.AvailablePositions);

				cache.Invalidate(1, 95);
				Assert.Empty(cache.LoadAvailable());
			} finally {
				if (File.Exists(cachePath))
					File.Delete(cachePath);
			}
		}

		[Fact]
		public void OrderedWorkerScheduleNeverReversesThePlannedOrder() {
			var first = OrderedWorkerSchedule.NextDueTime(TimeSpan.Zero, TimeSpan.FromSeconds(2));
			var second = OrderedWorkerSchedule.NextDueTime(first, TimeSpan.FromSeconds(5));
			var third = OrderedWorkerSchedule.NextDueTime(second, TimeSpan.FromSeconds(1));

			Assert.True(first < second);
			Assert.True(second < third);
			Assert.Equal(TimeSpan.FromSeconds(8), third);
		}

		[Fact]
		public void OrderedWorkerScheduleIgnoresAnInvalidNegativeDelay() {
			Assert.Equal(
				TimeSpan.FromSeconds(3),
				OrderedWorkerSchedule.NextDueTime(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(-1)));
		}

		[Fact]
		public void DiscoveryDispatchPlannerUsesEveryOriginBeforeReusingOne() {
			var planner = new DiscoveryDispatchPlanner(originCount: 13);
			var selected = new List<int>();

			for (var dispatch = 0; dispatch < 26; dispatch++) {
				var origin = planner.SelectNextOrigin();
				if (!origin.HasValue) {
					Assert.True(planner.BeginNextRound());
					origin = planner.SelectNextOrigin();
				}

				selected.Add(origin.Value);
				planner.MarkSent(origin.Value);
			}

			Assert.Equal(Enumerable.Range(0, 13).Concat(Enumerable.Range(0, 13)), selected);
		}

		[Fact]
		public void DiscoveryDispatchPlannerDoesNotRetryAnUnavailableOriginInTheSameCycle() {
			var planner = new DiscoveryDispatchPlanner(originCount: 3);

			Assert.Equal(0, planner.SelectNextOrigin());
			planner.MarkUnavailable(0);
			Assert.Equal(1, planner.SelectNextOrigin());
			planner.MarkUnavailable(1);
			Assert.Equal(2, planner.SelectNextOrigin());
			planner.MarkUnavailable(2);

			Assert.Null(planner.SelectNextOrigin());
			Assert.False(planner.BeginNextRound());
		}

		[Fact]
		public void DiscoveryDispatchPlannerCanRetryARejectedPositionAfterTheOtherOriginsHadTheirTurn() {
			var planner = new DiscoveryDispatchPlanner(originCount: 2);

			Assert.Equal(0, planner.SelectNextOrigin());
			planner.MarkPositionUnavailable(0);
			Assert.Equal(1, planner.SelectNextOrigin());
			planner.MarkPositionUnavailable(1);

			Assert.True(planner.BeginNextRound());
			Assert.Equal(0, planner.SelectNextOrigin());
		}

		[Fact]
		public void ExpeditionDispatchPlannerSkipsFailedOriginsAndUsesLaterOrigins() {
			var planner = new ExpeditionDispatchPlanner(new[] { 0, 0, 0, 0, 0, 0 }, 1);
			var dispatched = new List<int>();

			while (dispatched.Count < 3) {
				var origin = planner.SelectNextOrigin();
				Assert.True(origin.HasValue);

				if (origin.Value < 3)
					planner.MarkUnavailable(origin.Value);
				else {
					dispatched.Add(origin.Value);
					planner.MarkSent(origin.Value);
				}
			}

			Assert.Equal(new[] { 3, 4, 5 }, dispatched);
		}

		[Fact]
		public void ExpeditionDispatchPlannerReusesAHealthyOriginWhenFailedOriginsCannotFillTheRequest() {
			var planner = new ExpeditionDispatchPlanner(new[] { 0, 0 }, 2);
			var dispatched = new List<int>();

			var failedOrigin = planner.SelectNextOrigin();
			Assert.Equal(0, failedOrigin);
			planner.MarkUnavailable(failedOrigin.Value);

			while (dispatched.Count < 2) {
				var origin = planner.SelectNextOrigin();
				if (!origin.HasValue) {
					Assert.True(planner.BeginNextRound());
					origin = planner.SelectNextOrigin();
				}

				Assert.Equal(1, origin.Value);
				dispatched.Add(origin.Value);
				planner.MarkSent(origin.Value);
			}

			Assert.Equal(new[] { 1, 1 }, dispatched);
		}

		[Fact]
		public void ExpeditionDispatchPlannerSpreadsMultipleRoundsBeforeReusingAnOrigin() {
			var planner = new ExpeditionDispatchPlanner(new[] { 0, 0 }, 2);
			var dispatched = new List<int>();

			while (dispatched.Count < 4) {
				var origin = planner.SelectNextOrigin();
				if (!origin.HasValue) {
					Assert.True(planner.BeginNextRound());
					origin = planner.SelectNextOrigin();
				}

				dispatched.Add(origin.Value);
				planner.MarkSent(origin.Value);
			}

			Assert.Equal(new[] { 0, 1, 0, 1 }, dispatched);
		}

		[Fact]
		public void ExpeditionDispatchPlannerUsesRandomOrderOnlyAsTheRoundRobinTieBreaker() {
			// Origin 1 has the lowest real load and must win even though it is last in
			// the randomized tie-break order. Randomization cannot hide a valid origin.
			var planner = new ExpeditionDispatchPlanner(
				new[] { 1, 0, 1 },
				maxPerOrigin: 2,
				startIndex: 0,
				selectionOrder: new[] { 2, 0, 1 });

			Assert.Equal(1, planner.SelectNextOrigin());
		}

		[Fact]
		public void ExpeditionDispatchPlannerContinuesAfterAnOriginPreparationFailure() {
			var planner = new ExpeditionDispatchPlanner(
				new[] { 0, 0, 0 },
				maxPerOrigin: 1,
				startIndex: 0,
				selectionOrder: new[] { 0, 1, 2 });

			var failedOrigin = planner.SelectNextOrigin();
			Assert.Equal(0, failedOrigin);
			planner.MarkUnavailable(failedOrigin.Value);

			Assert.Equal(1, planner.SelectNextOrigin());
		}

		[Fact]
		public void ExpeditionDispatchPlannerWithFixedOrderFillsFromLaterOriginsAfterTheFirstOriginFails() {
			var planner = new ExpeditionDispatchPlanner(
				new[] { 0, 0, 0 },
				maxPerOrigin: 3,
				startIndex: 0,
				selectionOrder: new[] { 0, 1, 2 });
			var dispatched = new List<int>();

			var first = planner.SelectNextOrigin();
			Assert.Equal(0, first);
			planner.MarkUnavailable(first.Value);

			while (dispatched.Count < 6) {
				var origin = planner.SelectNextOrigin();
				if (!origin.HasValue) {
					Assert.True(planner.BeginNextRound());
					origin = planner.SelectNextOrigin();
				}

				Assert.NotEqual(0, origin.Value);
				dispatched.Add(origin.Value);
				planner.MarkSent(origin.Value);
			}

			Assert.Equal(new[] { 1, 2, 1, 2, 1, 2 }, dispatched);
		}

		[Fact]
		public void ExpeditionDispatchPlannerFixedOrderExhaustsEveryHealthyOriginAfterAnyFailures() {
			for (var originCount = 2; originCount <= 7; originCount++) {
				for (var maxPerOrigin = 1; maxPerOrigin <= 3; maxPerOrigin++) {
					for (var startIndex = 0; startIndex < originCount; startIndex++) {
						var failureMasks = 1 << originCount;
						for (var failureMask = 0; failureMask < failureMasks; failureMask++) {
							var planner = new ExpeditionDispatchPlanner(
								Enumerable.Repeat(0, originCount).ToArray(),
								maxPerOrigin,
								startIndex,
								Enumerable.Range(0, originCount).ToArray());
							var dispatched = new List<int>();

							while (true) {
								var origin = planner.SelectNextOrigin();
								if (!origin.HasValue) {
									if (!planner.BeginNextRound())
										break;
									continue;
								}

								if ((failureMask & (1 << origin.Value)) != 0)
									planner.MarkUnavailable(origin.Value);
								else {
									dispatched.Add(origin.Value);
									planner.MarkSent(origin.Value);
								}
							}

							for (var index = 0; index < originCount; index++) {
								var expected = (failureMask & (1 << index)) == 0
									? maxPerOrigin
									: 0;
								Assert.Equal(expected, dispatched.Count(value => value == index));
							}
						}
					}
				}
			}
		}

		[Fact]
		public void ExpeditionDispatchPlannerRemainsFairAcrossOneThousandDeterministicScenarios() {
			var random = new Random(20260826);

			for (var scenario = 0; scenario < 1000; scenario++) {
				var originCount = random.Next(1, 17);
				var maxPerOrigin = random.Next(1, 6);
				var initialCounts = Enumerable.Range(0, originCount)
					.Select(_ => random.Next(0, maxPerOrigin + 1))
					.ToArray();
				var unavailableMask = random.Next(0, 1 << originCount);
				var selectionOrder = Enumerable.Range(0, originCount)
					.OrderBy(_ => random.Next())
					.ToArray();
				var startIndex = random.Next(-originCount, originCount * 2);
				var planner = new ExpeditionDispatchPlanner(
					initialCounts,
					maxPerOrigin,
					startIndex,
					selectionOrder);
				var dispatched = new List<int>();
				var guard = 0;

				while (guard++ < originCount * maxPerOrigin * 3 + originCount) {
					var origin = planner.SelectNextOrigin();
					if (!origin.HasValue) {
						if (!planner.BeginNextRound())
							break;
						continue;
					}

					if ((unavailableMask & (1 << origin.Value)) != 0)
						planner.MarkUnavailable(origin.Value);
					else {
						dispatched.Add(origin.Value);
						planner.MarkSent(origin.Value);
					}
				}

				Assert.True(guard < originCount * maxPerOrigin * 3 + originCount + 1,
					$"Planner did not terminate in deterministic scenario {scenario}.");
				for (var index = 0; index < originCount; index++) {
					var expected = (unavailableMask & (1 << index)) == 0
						? maxPerOrigin - initialCounts[index]
						: 0;
					Assert.Equal(expected, dispatched.Count(value => value == index));
				}
			}
		}

		[Fact]
		public void ExpeditionDispatchPlannerDoesNotForgetLocalSendsWhenFleetSnapshotIsStale() {
			var planner = new ExpeditionDispatchPlanner(new[] { 0 }, 2);

			var origin = planner.SelectNextOrigin();
			Assert.Equal(0, origin);
			planner.MarkSent(origin.Value);
			planner.SynchronizeActiveCounts(new[] { 0 });

			Assert.True(planner.BeginNextRound());
			Assert.Equal(0, planner.SelectNextOrigin());
		}

		[Fact]
		public void ExpeditionDispatchPlannerStopsAfterEveryOriginFailsOnce() {
			var planner = new ExpeditionDispatchPlanner(new[] { 0, 0, 0 }, 1);

			for (var index = 0; index < 3; index++) {
				var origin = planner.SelectNextOrigin();
				Assert.Equal(index, origin);
				planner.MarkUnavailable(origin.Value);
			}

			Assert.Null(planner.SelectNextOrigin());
			Assert.False(planner.BeginNextRound());
		}

		[Fact]
		public void ExpeditionPayloadIsReducedAfterFuelAndCargoAreReserved() {
			Assert.Equal(7000, ExpeditionPayloadPolicy.GetDeuteriumPayload(
				requested: 20000,
				available: 12000,
				flightFuel: 5000,
				cargoCapacity: 100000));
			Assert.Equal(3000, ExpeditionPayloadPolicy.GetDeuteriumPayload(
				requested: 20000,
				available: 50000,
				flightFuel: 5000,
				cargoCapacity: 3000));
		}

		[Fact]
		public void FleetSnapshotPolicyPublishesAReplacementWithoutMutatingThePreviousSnapshot() {
			var existing = new List<Fleet> {
				new Fleet { ID = 10, Mission = Missions.Expedition }
			};
			var dispatched = new Fleet { ID = 11, Mission = Missions.Expedition };

			var updated = FleetSnapshotPolicy.AddDispatchedFleet(existing, dispatched);

			Assert.Single(existing);
			Assert.Equal(new[] { 10, 11 }, updated.Select(fleet => fleet.ID));
			Assert.NotSame(existing, updated);
			Assert.Same(dispatched, updated[1]);
		}

		[Fact]
		public void FleetSnapshotPolicyDoesNotDuplicateAnAlreadyPublishedFleet() {
			var existing = new List<Fleet> {
				new Fleet { ID = 10, Mission = Missions.Expedition }
			};

			var updated = FleetSnapshotPolicy.AddDispatchedFleet(existing, new Fleet { ID = 10 });

			Assert.Single(updated);
			Assert.Equal(10, updated[0].ID);
		}

		[Fact]
		public void JumpGateTargetResolverAcceptsTheNewObjectShape() {
			var settings = new Dictionary<string, object> {
				["Target"] = new Dictionary<string, object> {
					["Galaxy"] = 2,
					["System"] = 275,
					["Position"] = 8
				}
			};

			var target = JumpGateTargetResolver.Resolve(settings, out var usedLegacyArray);

			Assert.Equal(new JumpGateTarget(2, 275, 8), target);
			Assert.False(usedLegacyArray);
		}

		[Fact]
		public void JumpGateTargetResolverReadsTheLegacySingleItemArray() {
			var settings = new Dictionary<string, object> {
				["Target"] = new[] {
					(object) new Dictionary<string, object> {
						["Galaxy"] = 1,
						["System"] = 1,
						["Position"] = 1
					}
				}
			};

			var target = JumpGateTargetResolver.Resolve(settings, out var usedLegacyArray);

			Assert.Equal(new JumpGateTarget(1, 1, 1), target);
			Assert.True(usedLegacyArray);
		}

		[Fact]
		public void SettingsLoaderPreservesTheNewJumpGateTargetAsAnObject() {
			var parsed = SettingsService.GetMergedSettingsFromContents(
				"{\"Brain\":{\"AutoFleetJumpGate\":{\"Target\":{\"Galaxy\":2,\"System\":275,\"Position\":8}}}}",
				new List<string> { "{}" });
			var brain = (IDictionary<string, object>) parsed.Brain;
			var settings = (IDictionary<string, object>) brain["AutoFleetJumpGate"];

			var target = JumpGateTargetResolver.Resolve(settings, out var usedLegacyArray);

			Assert.Equal(new JumpGateTarget(2, 275, 8), target);
			Assert.False(usedLegacyArray);
		}

		[Fact]
		public void AutoFarmSettingsRefreshExpiredSystemDataAndReopenNonActionableTargets() {
			var parsed = SettingsService.GetMergedSettingsFromContents(
				"{\"AutoFarm\":{\"DaysToKeepOldSystemData\":1,\"StopAfterFullScan\":false}}",
				new List<string> { "{}" });
			var autoFarm = (IDictionary<string, object>) parsed.AutoFarm;
			var observedAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
			var snapshot = new AutoFarmSystemSnapshot {
				Galaxy = 1,
				System = 139,
				ObservedAtUtc = observedAt,
				IsEmpty = false
			};
			var report = new EspionageReport { ID = 42 };
			var target = new FarmTarget(
				new Planet { Coordinate = new Coordinate(1, 139, 7, Celestials.Planet) },
				FarmState.NotSuitable,
				report);

			Assert.Equal(1, SettingsService.GetSetting(autoFarm, "DaysToKeepOldSystemData", 7));
			Assert.False(SettingsService.GetSetting(autoFarm, "StopAfterFullScan", true));
			Assert.Equal(
				AutoFarmSystemCacheDecision.Refresh,
				AutoFarmSystemCachePolicy.GetDecision(
					snapshot,
					observedAt.AddHours(36),
					TimeSpan.FromDays(SettingsService.GetSetting(autoFarm, "DaysToKeepOldSystemData", 7)),
					TimeSpan.FromDays(30)));
			Assert.True(AutoFarmTargetPolicy.ResetForFreshSystemData(target));
			Assert.Equal(FarmState.ProbesPending, target.State);
			Assert.Same(report, target.Report);
		}

		[Fact]
		public void DiscoveryPlannerOnlyReturnsPositionsReportedByTheSystemView() {
			var positions = DiscoverySystemPlanner.SelectAvailablePositions(
				new[] { 1, 3, 7, 16 },
				1,
				new HashSet<int>(),
				3);

			Assert.Equal(new[] { 1, 3, 7 }, positions);
		}

		[Fact]
		public void DiscoveryPlannerSkipsBlacklistedPositionsAndResumesFromCursor() {
			var positions = DiscoverySystemPlanner.SelectAvailablePositions(
				new[] { 1, 3, 5, 9 },
				3,
				new HashSet<int> { 5 },
				3);

			Assert.Equal(new[] { 3, 9 }, positions);
		}

		[Fact]
		public void AutoFarmTreatsAPlanetsOnlySystemAsEmpty() {
			var planets = new[] {
				new Planet {
					Inactive = true,
					Vacation = true,
					Coordinate = new Coordinate(1, 1, 1, Celestials.Planet)
				}
			};

			Assert.True(AutoFarmSystemPolicy.IsEmptySystem(planets));
			Assert.Empty(AutoFarmSystemPolicy.GetEligibleTargets(planets));
		}

		[Fact]
		public void AutoFarmDoesNotUseTheLongCooldownForARegularInactivePlanet() {
			var planets = new[] {
				new Planet {
					Inactive = true,
					Vacation = false,
					Coordinate = new Coordinate(1, 1, 1, Celestials.Planet)
				}
			};

			Assert.False(AutoFarmSystemPolicy.IsEmptySystem(planets));
			Assert.Single(AutoFarmSystemPolicy.GetEligibleTargets(planets));
		}

		[Fact]
		public void AutoFarmDistinguishesARealEmptySystemFromAVisitedSystemWithoutTargets() {
			var activePlanet = new Planet {
				Inactive = false,
				Coordinate = new Coordinate(1, 1, 1, Celestials.Planet)
			};

			Assert.False(AutoFarmSystemPolicy.IsEmptySystem(new[] { activePlanet }));
		Assert.Empty(AutoFarmSystemPolicy.GetEligibleTargets(new[] { activePlanet }));
		Assert.True(AutoFarmSystemPolicy.IsEmptySystem(Array.Empty<Planet>()));
		}

		[Fact]
		public void AutoFarmKeepsEmptySystemsSkippedForThirtyDaysButUsesNormalDataForSevenDays() {
			var observedAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
			var emptySnapshot = new AutoFarmSystemSnapshot {
				ObservedAtUtc = observedAt,
				IsEmpty = true
			};
			var populatedSnapshot = new AutoFarmSystemSnapshot {
				ObservedAtUtc = observedAt,
				IsEmpty = false
			};

			Assert.Equal(
				AutoFarmSystemCacheDecision.Skip,
				AutoFarmSystemCachePolicy.GetDecision(emptySnapshot, observedAt.AddDays(14), TimeSpan.FromDays(7), TimeSpan.FromDays(30)));
			Assert.Equal(
				AutoFarmSystemCacheDecision.UseSnapshot,
				AutoFarmSystemCachePolicy.GetDecision(populatedSnapshot, observedAt.AddDays(6), TimeSpan.FromDays(7), TimeSpan.FromDays(30)));
			Assert.Equal(
				AutoFarmSystemCacheDecision.Refresh,
				AutoFarmSystemCachePolicy.GetDecision(populatedSnapshot, observedAt.AddDays(7), TimeSpan.FromDays(7), TimeSpan.FromDays(30)));
			Assert.Equal(
				AutoFarmSystemCacheDecision.Refresh,
				AutoFarmSystemCachePolicy.GetDecision(emptySnapshot, observedAt.AddDays(30), TimeSpan.FromDays(7), TimeSpan.FromDays(30)));
		}

		[Fact]
		public void AutoFarmResumesACompletedRangeWithoutSkippingSystemsBeforeTheCursor() {
			var systems = AutoFarmScanPlanner.EnumerateSystems(1, 5, 3);

			Assert.Equal(new[] { 3, 4, 5, 1, 2 }, systems);
		}

		[Fact]
		public void AutoFarmSlotBudgetCountsItsEspionageAndAttackFleets() {
			var fleets = new[] {
				new Fleet { Mission = Missions.Spy, Ships = new Ships(espionageProbe: 52) },
				new Fleet { Mission = Missions.Attack, Ships = new Ships(largeCargo: 25) },
				new Fleet { Mission = Missions.Attack, Ships = new Ships(largeCargo: 20) },
			};

			var budget = AutoFarmSlotPlanner.Calculate(
				maxSlots: 5,
				totalFreeSlots: 20,
				slotsToLeaveFree: 1,
				fleets,
				Buildables.LargeCargo);

			Assert.Equal(3, budget.OwnedSlots);
			Assert.Equal(2, budget.AvailableSlots);
		}

		[Fact]
		public void AutoFarmSlotBudgetNeverUsesASlotReservedForOtherWorkers() {
			var budget = AutoFarmSlotPlanner.Calculate(
				maxSlots: 5,
				totalFreeSlots: 1,
				slotsToLeaveFree: 1,
				Array.Empty<Fleet>(),
				Buildables.LargeCargo);

			Assert.Equal(0, budget.AvailableSlots);
		}

		[Fact]
		public void AutoFarmSlotBudgetDoesNotClaimMixedManualAttackFleets() {
			var fleets = new[] {
				new Fleet {
					Mission = Missions.Attack,
					Ships = new Ships(largeCargo: 25, battleship: 1)
				}
			};

			var budget = AutoFarmSlotPlanner.Calculate(
				maxSlots: 1,
				totalFreeSlots: 10,
				slotsToLeaveFree: 0,
				fleets,
				Buildables.LargeCargo);

			Assert.Equal(0, budget.OwnedSlots);
			Assert.Equal(1, budget.AvailableSlots);
		}

		[Fact]
		public void AutoFarmSlotBudgetCountsItsConfiguredEscortFleets() {
			var fleets = new[] {
				new Fleet {
					Mission = Missions.Attack,
					Ships = new Ships(largeCargo: 25, bomber: 4)
				},
				new Fleet {
					Mission = Missions.Attack,
					Ships = new Ships(largeCargo: 25, reaper: 2)
				},
				new Fleet {
					Mission = Missions.Attack,
					Ships = new Ships(largeCargo: 25, cruiser: 1)
				}
			};

			var budget = AutoFarmSlotPlanner.Calculate(
				maxSlots: 3,
				totalFreeSlots: 10,
				slotsToLeaveFree: 0,
				fleets,
				Buildables.LargeCargo,
				allowBombers: true,
				allowReapers: true);

			Assert.Equal(2, budget.OwnedSlots);
			Assert.Equal(1, budget.AvailableSlots);
		}

		[Fact]
		public void AutoFarmSlotBudgetDoesNotClaimManualEspionageFleets() {
			var fleets = new[] {
				new Fleet { ID = 10, Mission = Missions.Spy, Ships = new Ships(espionageProbe: 16) },
				new Fleet { ID = 11, Mission = Missions.Spy, Ships = new Ships(espionageProbe: 16) }
			};

			var budget = AutoFarmSlotPlanner.Calculate(
				maxSlots: 2,
				totalFreeSlots: 10,
				slotsToLeaveFree: 0,
				fleets,
				Buildables.LargeCargo,
				new HashSet<int> { 10 });

			Assert.Equal(1, budget.OwnedSlots);
			Assert.Equal(1, budget.AvailableSlots);
		}

		[Fact]
		public void AutoFarmTreatsAnAttackFromAnyOriginAsActive() {
			var target = new FarmTarget(
				new Planet { Coordinate = new Coordinate(2, 200, 8, Celestials.Planet) },
				FarmState.AttackSent);
			var fleets = new[] {
				new Fleet {
					Mission = Missions.Attack,
					Origin = new Coordinate(2, 10, 3, Celestials.Planet),
					Destination = new Coordinate(2, 200, 8, Celestials.Planet),
					ReturnFlight = true
				}
			};

			Assert.True(AutoFarmAttackPolicy.IsAttackInProgress(target, fleets));
		}

		[Fact]
		public void AutoFarmNeverReusesAConsumedEspionageReport() {
			var target = new FarmTarget(
				new Planet { Coordinate = new Coordinate(2, 200, 8, Celestials.Planet) },
				FarmState.ProbesPending) {
				ConsumedReportId = 42
			};

			Assert.False(AutoFarmAttackPolicy.CanProcessReport(target, new EspionageReport { ID = 42 }));
			Assert.True(AutoFarmAttackPolicy.CanProcessReport(target, new EspionageReport { ID = 43 }));
		}

		[Fact]
		public void AutoFarmDoesNotProcessReportsWhileThePreviousAttackIsStillSent() {
			var target = new FarmTarget(
				new Planet { Coordinate = new Coordinate(2, 200, 8, Celestials.Planet) },
				FarmState.AttackSent) {
				ConsumedReportId = 42
			};

			Assert.False(AutoFarmAttackPolicy.CanProcessReport(target, new EspionageReport { ID = 42 }));
			Assert.False(AutoFarmAttackPolicy.CanProcessReport(target, new EspionageReport { ID = 43 }));
		}

		[Fact]
		public void AutoFarmStateStorePersistsSystemSnapshotsAndPendingTargets() {
			var dataFolder = Path.Combine(Path.GetTempPath(), "tbot-tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dataFolder);

			try {
				var observedAt = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
				using (var store = new AutoFarmStateStore("autofarm.db", dataFolder)) {
					store.SaveSystemSnapshot(5, 123, observedAt, false, new List<Planet> {
						new Planet {
							Name = "Inactive Planet",
							Inactive = true,
							Coordinate = new Coordinate(5, 123, 7, Celestials.Planet)
						}
					});

					var persistedTarget = new FarmTarget(
						new Planet { Name = "Inactive Planet", Coordinate = new Coordinate(5, 123, 7, Celestials.Planet) },
						FarmState.AttackPending,
						new EspionageReport {
							Coordinate = new Coordinate(5, 123, 7, Celestials.Planet),
							Metal = 1000000,
							ID = 42
						}) {
						ConsumedReportId = 42
					};
					store.UpsertTarget(persistedTarget,
						observedAt);
				}

				using (var store = new AutoFarmStateStore("autofarm.db", dataFolder)) {
					var snapshot = store.GetFreshSystemSnapshot(5, 123, observedAt.AddDays(6), TimeSpan.FromDays(7));
					var targets = store.LoadTargets();

					Assert.NotNull(snapshot);
					Assert.Single(snapshot.Planets);
					Assert.Single(targets);
					Assert.Equal(FarmState.AttackPending, targets[0].State);
					Assert.Equal(1000000, targets[0].Report.Metal);
					Assert.Equal(42, targets[0].ConsumedReportId);
				}
			} finally {
				Directory.Delete(dataFolder, true);
			}
		}

		[Fact]
		public void AutoFarmStateStorePersistsConsumedReportsAndDeduplicatesAttackHistory() {
			var dataFolder = Path.Combine(Path.GetTempPath(), "tbot-tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dataFolder);

			try {
				var dispatchedAt = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
				var target = new FarmTarget(
					new Planet { Name = "Inactive Planet", Coordinate = new Coordinate(5, 123, 7, Celestials.Planet) },
					FarmState.AttackSent,
					new EspionageReport {
						Coordinate = new Coordinate(5, 123, 7, Celestials.Planet),
						ID = 99
					});

				using (var store = new AutoFarmStateStore("autofarm.db", dataFolder)) {
					store.RecordAttack(target, new Resources(metal: 100, crystal: 50, deuterium: 25), dispatchedAt);
					store.RecordAttack(target, new Resources(metal: 100, crystal: 50, deuterium: 25), dispatchedAt);
				}

				using (var store = new AutoFarmStateStore("autofarm.db", dataFolder)) {
					Assert.True(store.HasConsumedReport(99));
					var history = store.LoadAttackHistory();
					Assert.Single(history);
					Assert.Equal(99, history[0].ReportId);
					Assert.Equal(175, history[0].Loot.TotalResources);
				}
			} finally {
				Directory.Delete(dataFolder, true);
			}
		}

		[Fact]
		public void AutoFarmStateStorePersistsScanCursorAcrossRestart() {
			var dataFolder = Path.Combine(Path.GetTempPath(), "tbot-tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dataFolder);

			try {
				var updatedAt = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
				using (var store = new AutoFarmStateStore("autofarm.db", dataFolder))
					store.SaveScanCursor(0, 1, 139, updatedAt);

				using (var store = new AutoFarmStateStore("autofarm.db", dataFolder)) {
					var cursor = store.LoadScanCursor();
					Assert.NotNull(cursor);
					Assert.Equal(new AutoFarmScanCursor(0, 1, 139), cursor);
				}
			} finally {
				Directory.Delete(dataFolder, true);
			}
		}

		[Fact]
		public void AutoFarmStateStorePersistsProbeIdentityAndPendingReportPhase() {
			var dataFolder = Path.Combine(Path.GetTempPath(), "tbot-tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dataFolder);

			try {
				var coordinate = new Coordinate(1, 139, 7, Celestials.Planet);
				var updatedAt = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
				using (var store = new AutoFarmStateStore("autofarm.db", dataFolder)) {
					store.UpsertTarget(new FarmTarget(
						new Planet { Name = "Inactive Planet", Coordinate = coordinate },
						FarmState.ProbesSent) {
						ProbeFleetId = 7001,
						ProbeLevel = 2
					}, updatedAt);
					store.SaveScanCursor(0, 0, 0, updatedAt, reportPhasePending: true);
				}

				using (var store = new AutoFarmStateStore("autofarm.db", dataFolder)) {
					var target = Assert.Single(store.LoadTargets());
					var cursor = store.LoadScanCursor();
					Assert.Equal(7001, target.ProbeFleetId);
					Assert.Equal(2, target.ProbeLevel);
					Assert.NotNull(cursor);
					Assert.True(cursor.ReportPhasePending);
				}
			} finally {
				Directory.Delete(dataFolder, true);
			}
		}

		[Fact]
		public void AutoFarmDoesNotCommitAFullScanWhenReportProcessingWasNotCompleted() {
			Assert.False(AutoFarmCyclePolicy.CanFinalizeFullScan(true, false));
			Assert.True(AutoFarmCyclePolicy.CanFinalizeFullScan(true, true));
		}

		[Theory]
		[InlineData(FarmState.NotSuitable)]
		[InlineData(FarmState.ProbesPending)]
		[InlineData(FarmState.ProbesSent)]
		[InlineData(FarmState.AttackPending)]
		public void AutoFarmDoesNotReprocessTheSameReportWhenDeletionFails(FarmState state) {
			var coordinate = new Coordinate(1, 139, 7, Celestials.Planet);
			var target = new FarmTarget(
				new Planet { Name = "Inactive Planet", Coordinate = coordinate },
				state,
				new EspionageReport { ID = 42, Coordinate = coordinate });

			Assert.True(AutoFarmAttackPolicy.IsPreviouslyStoredReport(target,
				new EspionageReport { ID = 42, Coordinate = coordinate }));
			Assert.False(AutoFarmAttackPolicy.IsPreviouslyStoredReport(target,
				new EspionageReport { ID = 43, Coordinate = coordinate }));
		}

		[Fact]
		public void AutoFarmDoesNotCrashWhenAFoundTargetHasNoPreviousReport() {
			var coordinate = new Coordinate(1, 139, 7, Celestials.Planet);
			var target = new FarmTarget(
				new Planet { Name = "Inactive Planet", Coordinate = coordinate },
				FarmState.ProbesPending);

			Assert.False(AutoFarmAttackPolicy.IsPreviouslyStoredReport(target,
				new EspionageReport { ID = 42, Coordinate = coordinate }));
		}

		[Theory]
		[InlineData(FarmState.NotSuitable)]
		[InlineData(FarmState.ProbesRequired)]
		[InlineData(FarmState.FailedProbesRequired)]
		public void AutoFarmReopensAStaleNonActionableTargetWhenItsSystemIsRefreshed(FarmState state) {
			var coordinate = new Coordinate(1, 139, 7, Celestials.Planet);
			var report = new EspionageReport { ID = 42, Coordinate = coordinate };
			var target = new FarmTarget(
				new Planet { Name = "Inactive Planet", Coordinate = coordinate },
				state,
				report) {
				ConsumedReportId = 17
			};

			Assert.True(AutoFarmTargetPolicy.ResetForFreshSystemData(target));
			Assert.Equal(FarmState.ProbesPending, target.State);
			Assert.Same(report, target.Report);
			Assert.Equal(17, target.ConsumedReportId);
		}

		[Theory]
		[InlineData(FarmState.ProbesPending)]
		[InlineData(FarmState.ProbesSent)]
		[InlineData(FarmState.AttackPending)]
		[InlineData(FarmState.AttackSent)]
		public void AutoFarmDoesNotResetAnActiveOrAlreadyPendingTargetWhenItsSystemIsRefreshed(FarmState state) {
			var coordinate = new Coordinate(1, 139, 7, Celestials.Planet);
			var report = new EspionageReport { ID = 42, Coordinate = coordinate };
			var target = new FarmTarget(
				new Planet { Name = "Inactive Planet", Coordinate = coordinate },
				state,
				report);

			Assert.False(AutoFarmTargetPolicy.ResetForFreshSystemData(target));
			Assert.Equal(state, target.State);
			Assert.Same(report, target.Report);
		}

		[Fact]
		public void AutoFarmWaitsForTheConfiguredReportCooldownBeforeReprobingNotSuitableTargets() {
			var coordinate = new Coordinate(1, 139, 7, Celestials.Planet);
			var observedAt = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
			var target = new FarmTarget(
				new Planet { Name = "Inactive Planet", Coordinate = coordinate },
				FarmState.NotSuitable,
				new EspionageReport { ID = 42, Coordinate = coordinate, Date = observedAt });

			var isWaiting = AutoFarmTargetPolicy.IsWaitingForReportCooldown(
				target,
				observedAt.AddHours(12),
				TimeSpan.FromDays(1),
				out var nextProbeAtUtc);

			Assert.True(isWaiting);
			Assert.Equal(observedAt.AddDays(1), nextProbeAtUtc);
		}

		[Fact]
		public void AutoFarmReprobesNotSuitableTargetsAfterTheConfiguredReportCooldown() {
			var coordinate = new Coordinate(1, 139, 7, Celestials.Planet);
			var observedAt = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
			var target = new FarmTarget(
				new Planet { Name = "Inactive Planet", Coordinate = coordinate },
				FarmState.NotSuitable,
				new EspionageReport { ID = 42, Coordinate = coordinate, Date = observedAt });

			var isWaiting = AutoFarmTargetPolicy.IsWaitingForReportCooldown(
				target,
				observedAt.AddHours(24),
				TimeSpan.FromDays(1),
				out var nextProbeAtUtc);

			Assert.False(isWaiting);
			Assert.Equal(observedAt.AddDays(1), nextProbeAtUtc);
		}

		[Fact]
		public void AutoFarmDoesNotReopenARecentNotSuitableReportJustBecauseTheSystemWasRefreshed() {
			var coordinate = new Coordinate(1, 139, 7, Celestials.Planet);
			var observedAt = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
			var target = new FarmTarget(
				new Planet { Name = "Inactive Planet", Coordinate = coordinate },
				FarmState.NotSuitable,
				new EspionageReport { ID = 42, Coordinate = coordinate, Date = observedAt });

			Assert.False(AutoFarmTargetPolicy.ShouldReopenAfterSystemRefresh(
				target,
				observedAt.AddHours(12),
				TimeSpan.FromDays(1)));
			Assert.Equal(FarmState.NotSuitable, target.State);
		}

		[Fact]
		public void AutoFarmReopensAnOldNotSuitableReportAfterSystemRefresh() {
			var coordinate = new Coordinate(1, 139, 7, Celestials.Planet);
			var observedAt = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
			var target = new FarmTarget(
				new Planet { Name = "Inactive Planet", Coordinate = coordinate },
				FarmState.NotSuitable,
				new EspionageReport { ID = 42, Coordinate = coordinate, Date = observedAt });

			Assert.True(AutoFarmTargetPolicy.ShouldReopenAfterSystemRefresh(
				target,
				observedAt.AddHours(24),
				TimeSpan.FromDays(1)));
			Assert.True(AutoFarmTargetPolicy.ResetForFreshSystemData(target));
			Assert.Equal(FarmState.ProbesPending, target.State);
		}

		[Fact]
		public void AutoFarmDoesNotReopenARecentlyMissingProbe() {
			var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

			Assert.False(AutoFarmEspionagePolicy.ShouldReopenMissingProbe(
				now.AddMinutes(-5), now, TimeSpan.FromMinutes(10)));
			Assert.True(AutoFarmEspionagePolicy.ShouldReopenMissingProbe(
				now.AddMinutes(-11), now, TimeSpan.FromMinutes(10)));
		}

		[Fact]
		public void AutoFarmPersistsProcessedReportsIndependentlyFromAttackHistory() {
			var dataFolder = Path.Combine(Path.GetTempPath(), "tbot-tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dataFolder);

			try {
				using var store = new AutoFarmStateStore("autofarm-processed.db", dataFolder);
				var coordinate = new Coordinate(1, 139, 7, Celestials.Planet);

				Assert.False(store.HasProcessedReport(123));
				store.RecordProcessedReport(123, coordinate, "NotSuitable", DateTime.UtcNow);
				Assert.True(store.HasProcessedReport(123));
			} finally {
				Directory.Delete(dataFolder, true);
			}
		}

		[Fact]
		public void AutoFarmPersistsMalformedReportIdsEvenWhenNoCoordinateWasReturned() {
			var dataFolder = Path.Combine(Path.GetTempPath(), "tbot-tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dataFolder);

			try {
				using var store = new AutoFarmStateStore("autofarm-malformed-report.db", dataFolder);
				store.RecordProcessedReport(456, null, "Malformed", DateTime.UtcNow);

				Assert.True(store.HasProcessedReport(456));
			} finally {
				Directory.Delete(dataFolder, true);
			}
		}

		[Fact]
		public void AutoFarmReadsTheOutcomeOfAReportPreservedOutsideItsConfiguredRange() {
			var dataFolder = Path.Combine(Path.GetTempPath(), "tbot-tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dataFolder);

			try {
				using var store = new AutoFarmStateStore("autofarm-preserved-report.db", dataFolder);
				var coordinate = new Coordinate(1, 250, 6, Celestials.Planet);
				store.RecordProcessedReport(789, coordinate, "NotScanned", DateTime.UtcNow);

				Assert.Equal("NotScanned", store.GetProcessedReportOutcome(789));
				Assert.Null(store.GetProcessedReportOutcome(790));
				Assert.True(AutoFarmReportPolicy.IsPreservedOutsideConfiguredRange("NotScanned", false));
				Assert.False(AutoFarmReportPolicy.IsPreservedOutsideConfiguredRange("NotScanned", true));
			} finally {
				Directory.Delete(dataFolder, true);
			}
		}

		[Fact]
		public void AutoFarmMigratesProcessedReportIdsForAnOlderDatabase() {
			var dataFolder = Path.Combine(Path.GetTempPath(), "tbot-tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dataFolder);
			var databasePath = Path.Combine(dataFolder, "autofarm-old-schema.db");

			try {
				using (var connection = new SqliteConnection($"Data Source={databasePath}")) {
					connection.Open();
					using (var command = connection.CreateCommand()) {
						command.CommandText = @"
CREATE TABLE processed_reports (
  report_id INTEGER NOT NULL PRIMARY KEY,
  galaxy INTEGER NOT NULL,
  system INTEGER NOT NULL,
  position INTEGER NOT NULL,
  celestial_type INTEGER NOT NULL,
  outcome TEXT NOT NULL,
  processed_at_utc TEXT NOT NULL
);
INSERT INTO processed_reports (report_id, galaxy, system, position, celestial_type, outcome, processed_at_utc)
VALUES (321, 1, 245, 6, 1, 'NotSuitable', '2026-08-25T00:00:00.0000000Z');";
						command.ExecuteNonQuery();
					}
				}

				using (var store = new AutoFarmStateStore("autofarm-old-schema.db", dataFolder)) {
					Assert.True(store.HasProcessedReport(321));
					store.RecordProcessedReport(322, null, "Malformed", DateTime.UtcNow);
					Assert.True(store.HasProcessedReport(322));
				}
			} finally {
				SqliteConnection.ClearAllPools();
				Directory.Delete(dataFolder, true);
			}
		}

		[Fact]
		public void AutoFarmDashboardReaderShowsCurrentReportsAndAttackHistory() {
			var dataFolder = Path.Combine(AppContext.BaseDirectory, "data");
			Directory.CreateDirectory(dataFolder);
			var databasePath = Path.Combine(dataFolder, "autofarm_dashboard-test.db");

			try {
				var observedAt = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
				var target = new FarmTarget(
					new Planet { Name = "Dashboard Target", Coordinate = new Coordinate(5, 123, 7, Celestials.Planet) },
					FarmState.AttackSent,
					new EspionageReport {
						ID = 101,
						Date = observedAt,
						Coordinate = new Coordinate(5, 123, 7, Celestials.Planet),
						Metal = 1000,
						Crystal = 500,
						Deuterium = 250
					});

				using (var store = new AutoFarmStateStore("autofarm_dashboard-test.db", dataFolder)) {
					store.SaveSystemSnapshot(5, 123, observedAt, false, new List<Planet>());
					store.UpsertTarget(target, observedAt);
					store.RecordAttack(
						target,
						new Resources(metal: 100, crystal: 50, deuterium: 25),
						observedAt,
						new Coordinate(5, 120, 4, Celestials.Planet),
						Missions.Attack.ToString());
				}

				var dashboard = new AutoFarmDashboardReader().Read("dashboard-test");

				Assert.True(dashboard.DatabaseAvailable);
				Assert.Equal(1, dashboard.CachedSystemCount);
				Assert.Equal("Cached no targets", dashboard.Systems[0].Status);
				Assert.Single(dashboard.Reports);
				Assert.Equal("AttackSent", dashboard.Reports[0].State);
				Assert.Equal("Attack sent", dashboard.Reports[0].Reason);
				Assert.Single(dashboard.Attacks);
				Assert.Equal(101, dashboard.Attacks[0].ReportId);
				Assert.Equal("[P:5:120:4]", dashboard.Attacks[0].Origin);
				Assert.Equal("Attack", dashboard.Attacks[0].Mission);
			} finally {
				if (File.Exists(databasePath))
					File.Delete(databasePath);
			}
		}

		[Fact]
		public void AutoFarmDashboardExplainsWhyAReportWithDefenseWasKept() {
			var dataFolder = Path.Combine(AppContext.BaseDirectory, "data");
			Directory.CreateDirectory(dataFolder);
			var databasePath = Path.Combine(dataFolder, "autofarm_dashboard-defense-test.db");
			var observedAt = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
			var coordinate = new Coordinate(5, 123, 8, Celestials.Planet);
			var report = new EspionageReport {
				ID = 102,
				Date = observedAt,
				Coordinate = coordinate,
				HasDefensesInformation = true,
				RocketLauncher = 1
			};

			try {
				using (var store = new AutoFarmStateStore("autofarm_dashboard-defense-test.db", dataFolder)) {
					store.UpsertTarget(
						new FarmTarget(new Planet { Name = "Defended target", Coordinate = coordinate }, FarmState.NotSuitable, report),
						observedAt);
					store.RecordProcessedReport(report.ID, coordinate, AutoFarmReportPolicy.ManualReviewDefenseOutcome, observedAt);
				}

				var dashboard = new AutoFarmDashboardReader().Read("dashboard-defense-test");

				Assert.Single(dashboard.Reports);
				Assert.Equal("Manual review: defense detected", dashboard.Reports[0].Reason);
				Assert.Equal("AutoFarm kept this espionage report for manual handling", dashboard.Reports[0].ReasonDetail);
			} finally {
				if (File.Exists(databasePath))
					File.Delete(databasePath);
			}
		}

		[Fact]
		public void AutoFarmDashboardOnlyExposesAutoFarmLogs() {
			var logsPath = Path.Combine(Path.GetTempPath(), "tbot-tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(logsPath);
			var logFile = Path.Combine(logsPath, $"TBot{DateTime.Now:yyyyMMdd}.csv");
			var timestamp = DateTime.Now.ToString(CultureInfo.InvariantCulture);
			File.WriteAllText(logFile,
				$"type,sender,datetime,message{Environment.NewLine}" +
				$"Information,AutoFarm,{timestamp},AutoFarm decision{Environment.NewLine}" +
				$"Information,Expeditions,{timestamp},Expedition activity{Environment.NewLine}" +
				$"Error,FleetScheduler,{timestamp},Unrelated scheduler error{Environment.NewLine}");

			var previousLogsPath = SettingsService.LogsPath;
			try {
				SettingsService.LogsPath = logsPath;
				var dashboard = new AutoFarmDashboardReader().Read("log-filter-test");

				Assert.Single(dashboard.Logs);
				Assert.Equal("AutoFarm", dashboard.Logs[0].Sender);
				Assert.Empty(dashboard.Errors);
			} finally {
				SettingsService.LogsPath = previousLogsPath;
				Directory.Delete(logsPath, true);
			}
		}

		[Fact]
		public void AutoFarmStateStoreTreatsOldSystemSnapshotsAsStale() {
			var dataFolder = Path.Combine(Path.GetTempPath(), "tbot-tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dataFolder);

			try {
				var observedAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
				using var store = new AutoFarmStateStore("autofarm.db", dataFolder);
				store.SaveSystemSnapshot(5, 123, observedAt, true, new List<Planet>());

				Assert.Null(store.GetFreshSystemSnapshot(5, 123, observedAt.AddDays(7), TimeSpan.FromDays(7)));
			} finally {
				Directory.Delete(dataFolder, true);
			}
		}

		[Fact]
		public void AutoFarmRefreshesAOneAndAHalfDayOldSystemWhenTtlIsOneDay() {
			var observedAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
			var decision = AutoFarmSystemCachePolicy.GetDecision(
				new AutoFarmSystemSnapshot {
					Galaxy = 1,
					System = 139,
					ObservedAtUtc = observedAt,
					IsEmpty = false
				},
				observedAt.AddHours(36),
				TimeSpan.FromDays(1),
				TimeSpan.FromDays(30));

			Assert.Equal(AutoFarmSystemCacheDecision.Refresh, decision);
		}

		[Fact]
		public void ConstructionPlannerOrdersTheBestReturnFirstAndIgnoresIneligibleCandidates() {
			var ordered = AccountConstructionPlanner.Order(new[] {
				new ConstructionCandidate { CelestialId = 2, Priority = 0, Score = 4 },
				new ConstructionCandidate { CelestialId = 1, Priority = 0, Score = 2 },
				new ConstructionCandidate { CelestialId = 3, Priority = 0, Score = 1, Eligible = false }
			});

			Assert.Equal(new[] { 1, 2 }, ordered.Select(candidate => candidate.CelestialId));
		}

		[Fact]
		public void ConstructionPlannerPrioritizesUrgentAutoMineWorkBeforeMines() {
			Assert.True(AccountConstructionPlanner.GetAutoMinePriority(Buildables.FusionReactor) < AccountConstructionPlanner.GetAutoMinePriority(Buildables.MetalMine));
			Assert.True(AccountConstructionPlanner.GetAutoMinePriority(Buildables.Terraformer) < AccountConstructionPlanner.GetAutoMinePriority(Buildables.FusionReactor));
			Assert.True(AccountConstructionPlanner.GetAutoMinePriority(Buildables.MetalMine) < AccountConstructionPlanner.GetAutoMinePriority(Buildables.Null));
		}

		[Fact]
		public void ConstructionPlannerOrdersCelestialsByTheirBestCandidate() {
			var celestials = new[] {
				new Planet { ID = 3 },
				new Planet { ID = 1 },
				new Planet { ID = 2 }
			};
			var ordered = AccountConstructionPlanner.OrderCelestials(celestials, new[] {
				new ConstructionCandidate { CelestialId = 1, Priority = 0, Score = 5 },
				new ConstructionCandidate { CelestialId = 1, Priority = 0, Score = 3 },
				new ConstructionCandidate { CelestialId = 2, Priority = 0, Score = 1 }
			});

			Assert.Equal(new[] { 2, 1, 3 }, ordered.Select(celestial => celestial.ID));
		}

		[Fact]
		public void ResourcePlannerMergesDemandsForTheSameDestinationIntoOneShipment() {
			var plan = ResourceShipmentPlanner.Plan(
				new[] {
					new ResourceDemand(20, new Resources(metal: 100, crystal: 50), 0, "mine"),
					new ResourceDemand(20, new Resources(metal: 50, deuterium: 25), 1, "lifeform")
				},
				new[] { new ResourceSource(10, new Resources(metal: 150, crystal: 50, deuterium: 25), 225) });

			Assert.True(plan.IsComplete);
			Assert.Single(plan.Shipments);
			Assert.Equal(225, plan.Shipments[0].Amount.TotalResources);
			Assert.Equal(150, plan.Shipments[0].Amount.Metal);
		}

		[Fact]
		public void ResourcePlannerNeverReusesResourcesAcrossDestinations() {
			var plan = ResourceShipmentPlanner.Plan(
				new[] {
					new ResourceDemand(20, new Resources(metal: 100), 0),
					new ResourceDemand(30, new Resources(metal: 100), 1)
				},
				new[] { new ResourceSource(10, new Resources(metal: 150), 150) });

			Assert.Single(plan.Shipments);
			Assert.Single(plan.UnfulfilledDemands);
			Assert.Equal(20, plan.Shipments[0].DestinationId);
			Assert.Equal(100, plan.Shipments[0].Amount.Metal);
		}

		[Fact]
		public void ResourcePlannerDoesNotCommitPartialDemandWhenTheBatchCannotFundIt() {
			var plan = ResourceShipmentPlanner.Plan(
				new[] { new ResourceDemand(20, new Resources(metal: 100, crystal: 100)) },
				new[] { new ResourceSource(10, new Resources(metal: 100), 100) });

			Assert.False(plan.IsComplete);
			Assert.Empty(plan.Shipments);
			Assert.Single(plan.UnfulfilledDemands);
		}

		[Fact]
		public void ResourcePlannerIgnoresNegativeResourceAmounts() {
			var plan = ResourceShipmentPlanner.Plan(
				new[] { new ResourceDemand(20, new Resources(metal: -100, crystal: 50, deuterium: -10)) },
				new[] { new ResourceSource(10, new Resources(crystal: 50), 50) });

			Assert.True(plan.IsComplete);
			Assert.Single(plan.Shipments);
			Assert.Equal(50, plan.Shipments[0].Amount.Crystal);
			Assert.Equal(0, plan.Shipments[0].Amount.Metal);
			Assert.Equal(0, plan.Shipments[0].Amount.Deuterium);
		}

		[Fact]
		public void ResourcePlannerDoesNotExceedTheShipmentBudget() {
			var plan = ResourceShipmentPlanner.Plan(
				new[] { new ResourceDemand(20, new Resources(metal: 100)) },
				new[] {
					new ResourceSource(10, new Resources(metal: 50), 50),
					new ResourceSource(11, new Resources(metal: 50), 50)
				},
				maxShipments: 1);

			Assert.False(plan.IsComplete);
			Assert.Empty(plan.Shipments);
			Assert.Single(plan.UnfulfilledDemands);
		}

		[Fact]
		public void ResourcePlannerSplitsOneDemandAcrossOriginsWithoutLosingResources() {
			var plan = ResourceShipmentPlanner.Plan(
				new[] { new ResourceDemand(20, new Resources(metal: 300)) },
				new[] {
					new ResourceSource(10, new Resources(metal: 150), 150),
					new ResourceSource(11, new Resources(metal: 150), 150)
				});

			Assert.True(plan.IsComplete);
			Assert.Equal(2, plan.Shipments.Count);
			Assert.Equal(300, plan.Shipments.Sum(shipment => shipment.Amount.TotalResources));
		}

		[Fact]
		public void ResourcePlannerDoesNotConsumeSubthresholdSimulationCapacity() {
			var plan = ResourceShipmentPlanner.Plan(
				new[] {
					new ResourceDemand(20, new Resources(metal: 30, crystal: 70), 0),
					new ResourceDemand(21, new Resources(metal: 150), 1)
				},
				new[] {
					new ResourceSource(1, new Resources(metal: 100), 100),
					new ResourceSource(2, new Resources(metal: 30, crystal: 70), 100),
					new ResourceSource(3, new Resources(metal: 50), 50)
				},
				minimumShipmentResources: 50);

			Assert.True(plan.IsComplete);
			Assert.Equal(3, plan.Shipments.Count);
			Assert.Equal(100, plan.Shipments.Single(shipment => shipment.OriginId == 1 && shipment.DestinationId == 21).Amount.Metal);
			Assert.Equal(50, plan.Shipments.Single(shipment => shipment.OriginId == 3 && shipment.DestinationId == 21).Amount.Metal);
		}

		[Fact]
		public void AutoFarmDashboardReaderReadsLegacyAttackHistoryWithoutOriginColumns() {
			var dataFolder = Path.Combine(AppContext.BaseDirectory, "data");
			Directory.CreateDirectory(dataFolder);
			var databasePath = Path.Combine(dataFolder, "autofarm_legacy-dashboard.db");

			try {
				if (File.Exists(databasePath))
					File.Delete(databasePath);

				SQLitePCL.Batteries_V2.Init();
				using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False")) {
					connection.Open();
					using var command = connection.CreateCommand();
					command.CommandText = @"
CREATE TABLE systems (galaxy INTEGER, system INTEGER, observed_at_utc TEXT, is_empty INTEGER, planets_json TEXT);
CREATE TABLE targets (galaxy INTEGER, system INTEGER, position INTEGER, celestial_type INTEGER, name TEXT, state INTEGER, report_json TEXT, updated_at_utc TEXT);
CREATE TABLE attacks (report_id INTEGER PRIMARY KEY, galaxy INTEGER, system INTEGER, position INTEGER, celestial_type INTEGER, target_name TEXT, dispatched_at_utc TEXT, metal INTEGER, crystal INTEGER, deuterium INTEGER);
CREATE TABLE scan_cursor (id INTEGER PRIMARY KEY, range_index INTEGER, galaxy INTEGER, system INTEGER, updated_at_utc TEXT);
INSERT INTO systems VALUES (1, 139, '2026-08-10T12:00:00.0000000Z', 1, '[]');
INSERT INTO attacks VALUES (77, 1, 139, 7, 1, 'Legacy target', '2026-08-10T12:01:00.0000000Z', 100, 50, 25);";
					command.ExecuteNonQuery();
				}

				var dashboard = new AutoFarmDashboardReader().Read("legacy-dashboard");

				Assert.True(dashboard.DatabaseAvailable);
				Assert.Single(dashboard.Attacks);
				Assert.Equal("Unknown origin", dashboard.Attacks[0].Origin);
				Assert.Equal("Attack", dashboard.Attacks[0].Mission);
			} finally {
				if (File.Exists(databasePath))
					File.Delete(databasePath);
			}
		}

		[Fact]
		public void HarvestPlannerNeverMutatesItsCandidateCollectionWhileApplyingTheSlotBudget() {
			var origin = new HarvestOriginState(
				new Moon {
					Coordinate = new Coordinate(1, 400, 8, Celestials.Moon),
					Ships = new Ships(pathfinder: 10000)
				});
			var targets = new[] {
				new HarvestTarget(new Coordinate(1, 400, 16, Celestials.DeepSpace), new Resources(metal: 100000)),
				new HarvestTarget(new Coordinate(1, 401, 16, Celestials.DeepSpace), new Resources(metal: 100000)),
				new HarvestTarget(new Coordinate(1, 402, 16, Celestials.DeepSpace), new Resources(metal: 100000))
			};

			var plan = HarvestPlanner.Build(
				targets,
				new[] { origin },
				maxSlots: 2,
				(target, candidateOrigin) => 1000,
				(target, candidateOrigin) => Math.Abs(target.Destination.System - candidateOrigin.Celestial.Coordinate.System));

			Assert.Equal(2, plan.Count);
			Assert.Equal(8000, origin.AvailableShips(Buildables.Pathfinder));
		}

		[Fact]
		public void HarvestPlannerCanUseTheBestGlobalOriginInsteadOfTheOriginThatDiscoveredTheSystem() {
			var startMoon = new HarvestOriginState(
				new Moon {
					Coordinate = new Coordinate(1, 400, 8, Celestials.Moon),
					Ships = new Ships(pathfinder: 7000)
				});
			var localMoon = new HarvestOriginState(
				new Moon {
					Coordinate = new Coordinate(1, 415, 7, Celestials.Moon),
					Ships = new Ships()
				});
			var target = new HarvestTarget(
				new Coordinate(1, 415, 16, Celestials.DeepSpace),
				new Resources(metal: 700000));

			var plan = HarvestPlanner.Build(
				new[] { target },
				new[] { localMoon, startMoon },
				maxSlots: 1,
				(target, origin) => 7000,
				(target, origin) => Math.Abs(target.Destination.System - origin.Celestial.Coordinate.System));

			Assert.Single(plan);
			Assert.Same(startMoon.Celestial, plan[0].Origin);
			Assert.Equal(7000, plan[0].ShipsToSend);
		}

		[Fact]
		public void HarvestPlannerReservesPathfindersAndUsesTheRemainingShipsForTheNextTarget() {
			var origin = new HarvestOriginState(
				new Moon {
					Coordinate = new Coordinate(1, 400, 8, Celestials.Moon),
					Ships = new Ships(pathfinder: 7000)
				});
			var targets = new[] {
				new HarvestTarget(new Coordinate(1, 400, 16, Celestials.DeepSpace), new Resources(metal: 100000)),
				new HarvestTarget(new Coordinate(1, 401, 16, Celestials.DeepSpace), new Resources(metal: 100000))
			};

			var plan = HarvestPlanner.Build(
				targets,
				new[] { origin },
				maxSlots: 2,
				(target, candidateOrigin) => 5000,
				(target, candidateOrigin) => Math.Abs(target.Destination.System - candidateOrigin.Celestial.Coordinate.System));

			Assert.Equal(2, plan.Count);
			Assert.Equal(new long[] { 5000, 2000 }, plan.Select(item => item.ShipsToSend));
			Assert.Equal(0, origin.AvailableShips(Buildables.Pathfinder));
		}

		[Fact]
		public void HarvestPlannerUsesTheLargestAvailablePartialFleetWhenNoOriginCanCollectEverything() {
			var nearbyOrigin = new HarvestOriginState(
				new Moon {
					Coordinate = new Coordinate(1, 400, 8, Celestials.Moon),
					Ships = new Ships(pathfinder: 1000)
				});
			var largerOrigin = new HarvestOriginState(
				new Moon {
					Coordinate = new Coordinate(1, 450, 8, Celestials.Moon),
					Ships = new Ships(pathfinder: 7000)
				});

			var plan = HarvestPlanner.Build(
				new[] {
					new HarvestTarget(new Coordinate(1, 400, 16, Celestials.DeepSpace), new Resources(metal: 100000))
				},
				new[] { nearbyOrigin, largerOrigin },
				maxSlots: 1,
				(target, origin) => 10000,
				(target, origin) => Math.Abs(target.Destination.System - origin.Celestial.Coordinate.System));

			Assert.Single(plan);
			Assert.Same(largerOrigin.Celestial, plan[0].Origin);
			Assert.Equal(7000, plan[0].ShipsToSend);
		}
	}
}
