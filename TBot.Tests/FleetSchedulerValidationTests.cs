using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Common.Settings;
using Tbot.Includes;
using Tbot.Services;
using Tbot.Workers;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Exceptions;
using TBot.Ogame.Infrastructure.Models;
using TBot.Model;
using Xunit;

namespace TBot.Tests {
	public class FleetSchedulerValidationTests {
		[Fact]
		public async Task ValidatedDispatchRefreshesShipsBeforeCallingOGame() {
			var settings = SettingsService.GetMergedSettingsFromContents(
				"{\"General\":{\"SlotsToLeaveFree\":0},\"SleepMode\":{\"Active\":false}}",
				new List<string> { "{}" });
			var origin = new Planet {
				ID = 10,
				Coordinate = new Coordinate(1, 30, 8, Celestials.Planet),
				Resources = new Resources(0, 0, 100000)
			};
			var destination = new Coordinate(1, 31, 8, Celestials.Planet);
			var userData = CreateUserData();
			var parent = CreateParent(settings, userData);
			var getShipsCalls = 0;
			var ogame = Proxy<IOgameService>.Create((method, args) => {
				if (method.Name == nameof(IOgameService.GetShips)) {
					Interlocked.Increment(ref getShipsCalls);
					return Task.FromResult(new Ships(largeCargo: 10));
				}
				if (method.Name == nameof(IOgameService.SendFleet)) {
					return Task.FromResult(new Fleet {
						ID = 123,
						Mission = Missions.Attack,
						Origin = origin.Coordinate,
						Destination = destination,
						Ships = args[1] as Ships
					});
				}
				return Proxy<IOgameService>.Default(method.ReturnType);
			});

			var scheduler = CreateScheduler(parent, ogame);
			var result = await scheduler.SendValidatedFleet(
				origin,
				new Ships(largeCargo: 1),
				destination,
				Missions.Attack,
				Speeds.HundredPercent);

			Assert.Equal(123, result);
			Assert.Equal(1, getShipsCalls);
		}

		[Fact]
		public async Task ValidatedDispatchesAreSerializedAcrossWorkers() {
			var settings = SettingsService.GetMergedSettingsFromContents(
				"{\"General\":{\"SlotsToLeaveFree\":0},\"SleepMode\":{\"Active\":false}}",
				new List<string> { "{}" });
			var origin = new Planet {
				ID = 11,
				Coordinate = new Coordinate(1, 30, 8, Celestials.Planet),
				Resources = new Resources(0, 0, 100000)
			};
			var destination = new Coordinate(1, 31, 8, Celestials.Planet);
			var userData = CreateUserData();
			var parent = CreateParent(settings, userData);
			var probe = new ConcurrencyProbe();
			var ogame = Proxy<IOgameService>.Create((method, args) => {
				if (method.Name == nameof(IOgameService.GetShips))
					return Task.FromResult(new Ships(largeCargo: 10));

				if (method.Name == nameof(IOgameService.SendFleet))
					return SendFleetWithConcurrencyProbe(
						origin,
						destination,
						args[1] as Ships,
						probe);

				return Proxy<IOgameService>.Default(method.ReturnType);
			});

			var scheduler = CreateScheduler(parent, ogame);
			var sends = new List<Task<int>>();
			for (var i = 0; i < 16; i++) {
				sends.Add(scheduler.SendValidatedFleet(
					origin,
					new Ships(largeCargo: 1),
					destination,
					Missions.Attack,
					Speeds.HundredPercent));
			}

			var results = await Task.WhenAll(sends);

			Assert.All(results, result => Assert.True(result > 0));
			Assert.Equal(1, probe.MaxConcurrentSends);
		}

		[Fact]
		public async Task DiscoveryDispatchesAreSerializedAcrossWorkers() {
			var settings = SettingsService.GetMergedSettingsFromContents(
				"{\"General\":{\"SlotsToLeaveFree\":0},\"SleepMode\":{\"Active\":false}}",
				new List<string> { "{}" });
			var origin = new Planet {
				ID = 12,
				Coordinate = new Coordinate(1, 30, 8, Celestials.Planet)
			};
			var destination = new Coordinate(1, 31, 8, Celestials.Planet);
			var userData = CreateUserData();
			var parent = CreateParent(settings, userData);
			var probe = new ConcurrencyProbe();
			var ogame = Proxy<IOgameService>.Create((method, _) => {
				if (method.Name == nameof(IOgameService.SendDiscovery))
					return SendDiscoveryWithConcurrencyProbe(probe);

				return Proxy<IOgameService>.Default(method.ReturnType);
			});

			var scheduler = CreateScheduler(parent, ogame);
			var sends = new List<Task<DiscoveryDispatchResult>>();
			for (var i = 0; i < 16; i++)
				sends.Add(scheduler.SendDiscovery(origin, destination));
			var results = await Task.WhenAll(sends);

			Assert.All(results, result => Assert.Equal(DiscoveryDispatchStatus.Sent, result.Status));
			Assert.Equal(1, probe.MaxConcurrentSends);
		}

		[Fact]
		public async Task DiscoveryBusyResponseIsReportedWithoutRetryingTheStateChangingRequest() {
			var settings = SettingsService.GetMergedSettingsFromContents(
				"{\"General\":{\"SlotsToLeaveFree\":0},\"SleepMode\":{\"Active\":false}}",
				new List<string> { "{}" });
			var origin = new Planet {
				ID = 13,
				Coordinate = new Coordinate(1, 30, 8, Celestials.Planet)
			};
			var destination = new Coordinate(1, 31, 8, Celestials.Planet);
			var userData = CreateUserData();
			var parent = CreateParent(settings, userData);
			var sendCalls = 0;
			var ogame = Proxy<IOgameService>.Create((method, _) => {
				if (method.Name == nameof(IOgameService.SendDiscovery)) {
					Interlocked.Increment(ref sendCalls);
					return Task.FromException<bool>(new OgamedException(
						"An error has occurred: Status Code: BadRequest Status: error - Message: previous request still being processed (code 100)"));
				}

				return Proxy<IOgameService>.Default(method.ReturnType);
			});

			var scheduler = CreateScheduler(parent, ogame);
			var result = await scheduler.SendDiscovery(origin, destination);

			Assert.Equal(DiscoveryDispatchStatus.RequestPending, result.Status);
			Assert.Contains("code 100", result.Message);
			Assert.Equal(1, sendCalls);
		}

		[Fact]
		public async Task DiscoveryPathfinderShortageIsLocalToTheOrigin() {
			var settings = SettingsService.GetMergedSettingsFromContents(
				"{\"General\":{\"SlotsToLeaveFree\":0},\"SleepMode\":{\"Active\":false}}",
				new List<string> { "{}" });
			var origin = new Planet {
				ID = 131,
				Coordinate = new Coordinate(1, 30, 8, Celestials.Planet)
			};
			var destination = new Coordinate(1, 31, 8, Celestials.Planet);
			var parent = CreateParent(settings, CreateUserData());
			var ogame = Proxy<IOgameService>.Create((method, _) => {
				if (method.Name == nameof(IOgameService.SendDiscovery))
					return Task.FromException<bool>(new OgamedException(
						"An error has occurred: Status Code: BadRequest Status: error - Message: not enough pathfinders"));

				return Proxy<IOgameService>.Default(method.ReturnType);
			});

			var scheduler = CreateScheduler(parent, ogame);
			var result = await scheduler.SendDiscovery(origin, destination);

			Assert.Equal(DiscoveryDispatchStatus.NoShips, result.Status);
			Assert.Contains("pathfinders", result.Message);
		}

		[Fact]
		public async Task DiscoveryDoesNotCallOGameWhenTheFreshSlotSnapshotLeavesNoSlot() {
			var settings = SettingsService.GetMergedSettingsFromContents(
				"{\"General\":{\"SlotsToLeaveFree\":0},\"SleepMode\":{\"Active\":false}}",
				new List<string> { "{}" });
			var origin = new Planet {
				ID = 14,
				Coordinate = new Coordinate(1, 30, 8, Celestials.Planet)
			};
			var destination = new Coordinate(1, 31, 8, Celestials.Planet);
			var userData = CreateUserData();
			var parent = CreateParent(settings, userData);
			var sendCalls = 0;
			var ogame = Proxy<IOgameService>.Create((method, _) => {
				if (method.Name == nameof(IOgameService.SendDiscovery)) {
					Interlocked.Increment(ref sendCalls);
					return Task.FromResult(true);
				}

				return Proxy<IOgameService>.Default(method.ReturnType);
			});
			var calculation = Proxy<ICalculationService>.Create((method, _) => method.Name switch {
				nameof(ICalculationService.GetValidSpeedsForClass) => new List<decimal> { Speeds.HundredPercent },
				nameof(ICalculationService.CalcFleetPrediction) => new FleetPrediction { Time = 60, Fuel = 10 },
				nameof(ICalculationService.CalcFleetFuelCapacity) => 0L,
				_ => Proxy<ICalculationService>.Default(method.ReturnType)
			});
			var bridge = Proxy<ITBotOgamedBridge>.Create((method, _) => {
				if (method.Name == nameof(ITBotOgamedBridge.UpdateSlots))
					return Task.FromResult(new Slots { Total = 1, InUse = 1 });
				return Proxy<ITBotOgamedBridge>.Default(method.ReturnType);
			});
			var scheduler = new FleetScheduler(calculation, ogame);
			scheduler.SetTBotInstance(parent);
			scheduler.SetTBotOgameBridge(bridge);

			var result = await scheduler.SendDiscovery(origin, destination);

			Assert.Equal(DiscoveryDispatchStatus.NoSlots, result.Status);
			Assert.Equal(0, sendCalls);
		}

		[Fact]
		public async Task FleetSnapshotFreshnessStaysAttachedToTheMatchingSnapshotWhenRefreshesOverlap() {
			var settings = SettingsService.GetMergedSettingsFromContents(
				"{\"General\":{\"SlotsToLeaveFree\":0},\"SleepMode\":{\"Active\":false}}",
				new List<string> { "{}" });
			var userData = CreateUserData();
			var parent = CreateParent(settings, userData);
			var calls = 0;
			var ogame = Proxy<IOgameService>.Create((method, _) => {
				if (method.Name == nameof(IOgameService.GetFleets)) {
					var call = Interlocked.Increment(ref calls);
					return call == 1
						? Task.FromResult(new List<Fleet>())
						: Task.FromException<List<Fleet>>(new InvalidOperationException("simulated refresh failure"));
				}

				return Proxy<IOgameService>.Default(method.ReturnType);
			});

			var scheduler = CreateScheduler(parent, ogame);
		FleetSnapshotResult[] results = await Task.WhenAll(
				scheduler.UpdateFleetsWithFreshness(),
				scheduler.UpdateFleetsWithFreshness());

			Assert.Equal(2, calls);
			Assert.True(results.Any(result => result.IsFresh && result.Fleets != null));
			Assert.True(results.Any(result => !result.IsFresh && result.Fleets != null));
		}

		private static Task<Fleet> SendFleetWithConcurrencyProbe(
			Celestial origin,
			Coordinate destination,
			Ships ships,
			ConcurrencyProbe probe) {
			var active = Interlocked.Increment(ref probe.ActiveSends);
			var observedMax = Volatile.Read(ref probe.MaxConcurrentSends);
			while (active > observedMax) {
				var previous = Interlocked.CompareExchange(ref probe.MaxConcurrentSends, active, observedMax);
				if (previous == observedMax)
					break;
				observedMax = previous;
			}

			return CompleteSendAsync();

			async Task<Fleet> CompleteSendAsync() {
				await Task.Delay(2);
				Interlocked.Decrement(ref probe.ActiveSends);
				return new Fleet {
					ID = Interlocked.Increment(ref probe.NextFleetId),
					Mission = Missions.Attack,
					Origin = origin.Coordinate,
					Destination = destination,
					Ships = ships
				};
			}
		}

		private static Task<bool> SendDiscoveryWithConcurrencyProbe(ConcurrencyProbe probe) {
			var active = Interlocked.Increment(ref probe.ActiveSends);
			var observedMax = Volatile.Read(ref probe.MaxConcurrentSends);
			while (active > observedMax) {
				var previous = Interlocked.CompareExchange(ref probe.MaxConcurrentSends, active, observedMax);
				if (previous == observedMax)
					break;
				observedMax = previous;
			}

			return CompleteSendAsync();

			async Task<bool> CompleteSendAsync() {
				await Task.Delay(2);
				Interlocked.Decrement(ref probe.ActiveSends);
				return true;
			}
		}

		private sealed class ConcurrencyProbe {
			public int ActiveSends;
			public int MaxConcurrentSends;
			public int NextFleetId = 1000;
		}

		private static FleetScheduler CreateScheduler(ITBotMain parent, IOgameService ogame) {
			var calculation = Proxy<ICalculationService>.Create((method, _) => method.Name switch {
				nameof(ICalculationService.GetValidSpeedsForClass) => new List<decimal> { Speeds.HundredPercent },
				nameof(ICalculationService.CalcFleetPrediction) => new FleetPrediction { Time = 60, Fuel = 10 },
				nameof(ICalculationService.CalcFleetFuelCapacity) => 0L,
				_ => Proxy<ICalculationService>.Default(method.ReturnType)
			});
			var bridge = Proxy<ITBotOgamedBridge>.Create((method, args) => {
				if (method.Name == nameof(ITBotOgamedBridge.UpdatePlanet)) {
					var updated = args[0] as Celestial;
					updated.Resources ??= new Resources(0, 0, 100000);
					updated.LFBonuses ??= new LFBonuses();
					return Task.FromResult(updated);
				}
				if (method.Name == nameof(ITBotOgamedBridge.UpdateSlots))
					return Task.FromResult(new Slots { Total = 100, InUse = 0 });
				return Proxy<ITBotOgamedBridge>.Default(method.ReturnType);
			});

			var scheduler = new FleetScheduler(calculation, ogame);
			scheduler.SetTBotInstance(parent);
			scheduler.SetTBotOgameBridge(bridge);
			return scheduler;
		}

		private static UserData CreateUserData() {
			return new UserData {
				fleets = new List<Fleet>(),
				slots = new Slots { Total = 100, InUse = 0 },
				serverData = new ServerData { Galaxies = 5, Systems = 499 },
				researches = new Researches(),
				userInfo = new UserInfo()
			};
		}

		private static ITBotMain CreateParent(dynamic settings, UserData userData) {
			return Proxy<ITBotMain>.Create((method, _) => method.Name switch {
				"get_InstanceSettings" => settings,
				"get_InstanceAlias" => "FleetSchedulerTests",
				"get_UserData" => userData,
				_ => Proxy<ITBotMain>.Default(method.ReturnType)
			});
		}

		private class Proxy<T> : DispatchProxy where T : class {
			private Func<MethodInfo, object[], object> _handler;

			public static T Create(Func<MethodInfo, object[], object> handler) {
				var proxy = DispatchProxy.Create<T, Proxy<T>>();
				((Proxy<T>)(object)proxy)._handler = handler;
				return proxy;
			}

			public static object Default(Type returnType) {
				if (returnType == typeof(void))
					return null;
				if (returnType == typeof(Task))
					return Task.CompletedTask;
				if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>)) {
					var valueType = returnType.GetGenericArguments()[0];
					var value = valueType.IsValueType ? Activator.CreateInstance(valueType) : null;
					return typeof(Task)
						.GetMethod(nameof(Task.FromResult))
						.MakeGenericMethod(valueType)
						.Invoke(null, new[] { value });
				}
				if (returnType.IsValueType)
					return Activator.CreateInstance(returnType);
				return null;
			}

			protected override object Invoke(MethodInfo targetMethod, object[] args) {
				var result = _handler?.Invoke(targetMethod, args ?? Array.Empty<object>());
				return result ?? Default(targetMethod.ReturnType);
			}
		}
	}
}
