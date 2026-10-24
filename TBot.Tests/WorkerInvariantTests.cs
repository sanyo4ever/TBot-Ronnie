using System;
using System.Collections.Generic;
using System.Linq;
using TBot.Model;
using Tbot.Workers;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using Xunit;

namespace TBot.Tests {
	public class WorkerInvariantTests {
		[Fact]
		public void DiscoverySelectionUsesOneSharedSystemAndChoosesTheNearestOrigin() {
			var origins = new[] { 10, 80, 250 };

			Assert.Equal(1, DiscoveryOriginSelectionPolicy.SelectClosest(origins, 75, 499, 0));
			Assert.Equal(0, DiscoveryOriginSelectionPolicy.SelectClosest(origins, 12, 499, 0));
			Assert.Equal(2, DiscoveryOriginSelectionPolicy.SelectClosest(origins, 250, 499, 0));
			Assert.Equal(2, DiscoveryOriginSelectionPolicy.SelectClosest(
				origins, 10, 499, 0, new HashSet<int> { 0, 1 }));
		}

		[Fact]
		public void ExpeditionPlannerRemainsFairForManyOrdersAndTwoCompleteRounds() {
			for (int seed = 0; seed < 100; seed++) {
				const int originCount = 13;
				var order = Enumerable.Range(0, originCount)
					.OrderBy(index => ((index * 7919) + (seed * 104729)) % 1009)
					.ToArray();
				var planner = new ExpeditionDispatchPlanner(
					Enumerable.Repeat(0, originCount).ToArray(),
					maxPerOrigin: 2,
					startIndex: seed % originCount,
					selectionOrder: order);
				var sent = new int[originCount];

				for (int dispatch = 0; dispatch < originCount * 2; dispatch++) {
					var selected = planner.SelectNextOrigin();
					if (!selected.HasValue) {
						Assert.True(planner.BeginNextRound());
						selected = planner.SelectNextOrigin();
					}

					Assert.True(selected.HasValue);
					planner.MarkSent(selected.Value);
					sent[selected.Value]++;
				}

				Assert.Equal(Enumerable.Repeat(2, originCount), sent);
				Assert.Null(planner.SelectNextOrigin());
			}
		}

		[Fact]
		public void ExpeditionPlannerNeverExceedsTheConfiguredCapAcrossAdversarialSnapshots() {
			for (int originCount = 1; originCount <= 20; originCount++) {
				for (int maxPerOrigin = 0; maxPerOrigin <= 5; maxPerOrigin++) {
					var active = Enumerable.Range(0, originCount)
						.Select(index => maxPerOrigin == 0
							? 0
							: (index * 17 + originCount * 11) % (maxPerOrigin + 1))
						.ToArray();
					var selectionOrder = Enumerable.Range(0, originCount)
						.OrderBy(index => (index * 7919 + originCount * 104729 + maxPerOrigin * 97) % 1009)
						.ToArray();
					var planner = new ExpeditionDispatchPlanner(
						active,
						maxPerOrigin,
						startIndex: (originCount * 3 + maxPerOrigin) % originCount,
						selectionOrder: selectionOrder);
					var dispatched = new int[originCount];
					var expectedDispatches = active.Sum(count => maxPerOrigin - count);

					for (int attempt = 0; attempt < expectedDispatches + originCount * 3 + 3; attempt++) {
						var selected = planner.SelectNextOrigin();
						if (!selected.HasValue) {
							if (!planner.BeginNextRound())
								break;
							continue;
						}

						var originIndex = selected.Value;
						Assert.InRange(originIndex, 0, originCount - 1);
						Assert.True(active[originIndex] + dispatched[originIndex] < maxPerOrigin,
							$"Origin {originIndex} exceeded its cap ({maxPerOrigin}) for {originCount} origins.");
						dispatched[originIndex]++;
						planner.MarkSent(originIndex);
					}

					Assert.Equal(expectedDispatches, dispatched.Sum());
					Assert.All(Enumerable.Range(0, originCount), index =>
						Assert.Equal(maxPerOrigin, active[index] + dispatched[index]));
					Assert.Null(planner.SelectNextOrigin());
				}
			}
		}

		[Fact]
		public void ExpeditionPlannerSkipsUnavailableOriginsWithoutChangingHealthyCapacity() {
			const int originCount = 13;
			var planner = new ExpeditionDispatchPlanner(
				Enumerable.Repeat(0, originCount).ToArray(),
				maxPerOrigin: 2,
				selectionOrder: Enumerable.Range(0, originCount).Reverse().ToArray());
			planner.MarkUnavailable(5);
			var sent = new List<int>();

			for (int dispatch = 0; dispatch < 24; dispatch++) {
				var selected = planner.SelectNextOrigin();
				if (!selected.HasValue) {
					Assert.True(planner.BeginNextRound());
					selected = planner.SelectNextOrigin();
				}
				Assert.True(selected.HasValue);
				Assert.NotEqual(5, selected.Value);
				planner.MarkSent(selected.Value);
				sent.Add(selected.Value);
			}

			Assert.Equal(24, sent.Count);
			Assert.DoesNotContain(5, sent);
			Assert.All(Enumerable.Range(0, originCount).Where(index => index != 5),
				index => Assert.Equal(2, sent.Count(value => value == index)));
			Assert.Null(planner.SelectNextOrigin());
		}

		[Fact]
		public void ExpeditionPlannerNeverLosesLocalDispatchesWhenServerSnapshotIsStale() {
			var planner = new ExpeditionDispatchPlanner(new[] { 0, 0, 0 }, maxPerOrigin: 2);
			var first = planner.SelectNextOrigin();
			Assert.True(first.HasValue);
			planner.MarkSent(first.Value);
			planner.SynchronizeActiveCounts(new[] { 0, 0, 0 });

			var next = planner.SelectNextOrigin();
			Assert.True(next.HasValue);
			Assert.NotEqual(first.Value, next.Value);
		}

		[Theory]
		[InlineData(100, 30, 10, 100, 10)]
		[InlineData(100, 30, 80, 100, 70)]
		[InlineData(100, 30, 0, 20, 0)]
		[InlineData(100, 0, 30, 100, 30)]
		[InlineData(10, 100, 5, 100, 0)]
		public void ExpeditionFuelPayloadAlwaysReservesFuelAndRespectsCargo(
			long available,
			long flightFuel,
			long requested,
			long cargoCapacity,
			long expected) {
			var payload = ExpeditionPayloadPolicy.GetDeuteriumPayload(
				requested,
				available,
				flightFuel,
				cargoCapacity);

			Assert.Equal(expected, payload);
			Assert.InRange(payload, 0, Math.Max(0, Math.Min(requested, cargoCapacity)));
			Assert.True(payload <= Math.Max(0, available - flightFuel));
		}

		[Fact]
		public void AutoFarmSlotBudgetCountsOnlyOwnedPureFleets() {
			var fleets = new[] {
				new Fleet { ID = 1, Mission = Missions.Attack, Ships = new Ships(largeCargo: 10) },
				new Fleet { ID = 2, Mission = Missions.Attack, Ships = new Ships(largeCargo: 10, recycler: 1) },
				new Fleet { ID = 3, Mission = Missions.Attack, Ships = new Ships(cruiser: 1) },
				new Fleet { ID = 4, Mission = Missions.Spy, Ships = new Ships(espionageProbe: 10) },
				new Fleet { ID = 5, Mission = Missions.Spy, Ships = new Ships(espionageProbe: 10) }
			};

			var budget = AutoFarmSlotPlanner.Calculate(
				maxSlots: 5,
				totalFreeSlots: 10,
				slotsToLeaveFree: 1,
				fleets,
				Buildables.LargeCargo,
				new HashSet<int> { 5 });

			Assert.Equal(2, budget.OwnedSlots);
			Assert.Equal(3, budget.AvailableSlots);
		}

		[Fact]
		public void AutoFarmProbeCargoValidationUsesServerProbeCapacity() {
			const long probeCount = 537351;
			const long reportedLoot = 4396503;
			var serverData = new ServerData {
				CargoHyperspaceTechMultiplier = 0,
				ProbeCargo = 10
			};
			var fleet = new Ships(espionageProbe: probeCount);
			var calculation = new Tbot.Includes.CalculationService(null, null);

			var capacity = calculation.CalcFleetCapacity(
				fleet,
				serverData,
				hyperspaceTech: 0,
				lfBonuses: new LFBonuses(),
				playerClass: CharacterClass.NoClass,
				probeCargo: serverData.ProbeCargo);

			Assert.Equal(probeCount * 9, capacity);
			Assert.True(capacity >= reportedLoot);
		}

		[Fact]
		public void AutoFarmCacheKeepsEmptySystemsLongerThanNormalSystems() {
			var now = DateTime.UtcNow;
			var empty = new AutoFarmSystemSnapshot {
				Galaxy = 1,
				System = 1,
				ObservedAtUtc = now.AddDays(-10),
				IsEmpty = true
			};
			var populated = new AutoFarmSystemSnapshot {
				Galaxy = 1,
				System = 2,
				ObservedAtUtc = now.AddDays(-2),
				IsEmpty = false
			};

			Assert.Equal(
				AutoFarmSystemCacheDecision.Skip,
				AutoFarmSystemCachePolicy.GetDecision(empty, now, TimeSpan.FromDays(7), TimeSpan.FromDays(30)));
			Assert.Equal(
				AutoFarmSystemCacheDecision.UseSnapshot,
				AutoFarmSystemCachePolicy.GetDecision(populated, now, TimeSpan.FromDays(7), TimeSpan.FromDays(30)));
			Assert.Equal(
				AutoFarmSystemCacheDecision.Refresh,
				AutoFarmSystemCachePolicy.GetDecision(populated, now, TimeSpan.FromDays(1), TimeSpan.FromDays(30)));
		}

		[Fact]
		public void AutoFarmDoesNotCommitTheNextScanUntilReportsAreProcessed() {
			Assert.False(AutoFarmCyclePolicy.CanFinalizeFullScan(true, false));
			Assert.True(AutoFarmCyclePolicy.CanFinalizeFullScan(true, true));
			Assert.False(AutoFarmCyclePolicy.CanFinalizeFullScan(false, true));
		}
	}
}
