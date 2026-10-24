using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace TBot.Model {
	public sealed class DiscoverySystemCacheEntry {
		public int Galaxy { get; set; }
		public int System { get; set; }
		public List<int> AvailablePositions { get; set; } = new();
		public DateTime ObservedAtUtc { get; set; }

		[JsonIgnore]
		public bool HasAvailablePositions => AvailablePositions?.Count > 0;

		public DiscoverySystemCacheEntry Clone() {
			return new DiscoverySystemCacheEntry {
				Galaxy = Galaxy,
				System = System,
				AvailablePositions = (AvailablePositions ?? new List<int>()).ToList(),
				ObservedAtUtc = ObservedAtUtc
			};
		}
	}

	/// <summary>
	/// Remembers only systems that have at least one available discovery position.
	/// A cached entry is always revalidated before dispatch, so this file is a
	/// candidate index and never a source of truth.
	/// </summary>
	public sealed class DiscoverySystemCacheStore {
		private readonly object _lock = new();
		private readonly string _filePath;
		private Dictionary<string, DiscoverySystemCacheEntry> _entries = new(StringComparer.Ordinal);

		public DiscoverySystemCacheStore(string instanceAlias) {
			var dataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
			Directory.CreateDirectory(dataFolder);
			var safeAlias = new string((instanceAlias ?? "default")
				.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)
				.ToArray());
			_filePath = Path.Combine(dataFolder, $"autodiscovery_systems_{safeAlias}.json");
			Load();
		}

		public IReadOnlyList<DiscoverySystemCacheEntry> LoadAvailable() {
			lock (_lock)
				return _entries.Values.Where(entry => entry.HasAvailablePositions).Select(entry => entry.Clone()).ToList();
		}

		public void Observe(int galaxy, int system, IEnumerable<int> positions, DateTime observedAtUtc) {
			var normalized = (positions ?? Enumerable.Empty<int>())
				.Where(position => position >= 1 && position <= 15)
				.Distinct()
				.OrderBy(position => position)
				.ToList();

			lock (_lock) {
				var key = Key(galaxy, system);
				if (normalized.Count == 0)
					_entries.Remove(key);
				else
					_entries[key] = new DiscoverySystemCacheEntry {
						Galaxy = galaxy,
						System = system,
						AvailablePositions = normalized,
						ObservedAtUtc = observedAtUtc.ToUniversalTime()
					};
				Persist();
			}
		}

		public void Consume(int galaxy, int system, IEnumerable<int> positions) {
			var consumed = new HashSet<int>(positions ?? Enumerable.Empty<int>());
			if (consumed.Count == 0)
				return;

			lock (_lock) {
				if (!_entries.TryGetValue(Key(galaxy, system), out var entry))
					return;

				entry.AvailablePositions = (entry.AvailablePositions ?? new List<int>())
					.Where(position => !consumed.Contains(position))
					.ToList();
				if (entry.AvailablePositions.Count == 0)
					_entries.Remove(Key(galaxy, system));
				Persist();
			}
		}

		public void Invalidate(int galaxy, int system) {
			lock (_lock) {
				_entries.Remove(Key(galaxy, system));
				Persist();
			}
		}

		private static string Key(int galaxy, int system) => $"{galaxy}:{system}";

		private void Load() {
			if (!File.Exists(_filePath))
				return;

			try {
				var loaded = JsonConvert.DeserializeObject<Dictionary<string, DiscoverySystemCacheEntry>>(
					File.ReadAllText(_filePath));
				if (loaded != null)
					_entries = new Dictionary<string, DiscoverySystemCacheEntry>(loaded, StringComparer.Ordinal);
			} catch {
				_entries = new Dictionary<string, DiscoverySystemCacheEntry>(StringComparer.Ordinal);
			}
		}

		private void Persist() {
			try {
				var tempPath = _filePath + ".tmp";
				File.WriteAllText(tempPath, JsonConvert.SerializeObject(_entries, Formatting.Indented));
				if (File.Exists(_filePath)) {
					try {
						File.Replace(tempPath, _filePath, null);
					} catch {
						File.Delete(_filePath);
						File.Move(tempPath, _filePath);
					}
				} else {
					File.Move(tempPath, _filePath);
				}
			} catch {
				// The cache is an optimization; discovery must continue from the cursor.
			}
		}
	}

	public sealed record DiscoveryOriginSystemPair(
		int OriginIndex,
		int Galaxy,
		int System,
		int Distance);

	/// <summary>
	/// Selects the globally nearest valid origin/system pair. Round-robin is used
	/// only to make equal-distance choices deterministic and fair.
	/// </summary>
	public static class DiscoveryOriginSystemSelectionPolicy {
		public static DiscoveryOriginSystemPair SelectClosest(
			IReadOnlyList<(int Galaxy, int System)> origins,
			IReadOnlyList<(int Galaxy, int System)> systems,
			int maxSystem,
			int roundRobinStartIndex = 0,
			ISet<int> excludedOrigins = null,
			ISet<string> excludedSystems = null) {
			if (origins == null || origins.Count == 0 || systems == null || systems.Count == 0 || maxSystem <= 0)
				return null;

			var start = NormalizeIndex(roundRobinStartIndex, origins.Count);
			return systems
				.SelectMany(system => origins
					.Select((origin, originIndex) => new { system, origin, originIndex }))
				.Where(candidate => (excludedOrigins == null || !excludedOrigins.Contains(candidate.originIndex))
					&& (excludedSystems == null || !excludedSystems.Contains(Key(candidate.system.Galaxy, candidate.system.System)))
					&& candidate.origin.Galaxy == candidate.system.Galaxy)
				.Select(candidate => new {
					candidate.originIndex,
					candidate.system.Galaxy,
					candidate.system.System,
					distance = CircularDistance(candidate.origin.System, candidate.system.System, maxSystem),
					tieBreak = (candidate.originIndex - start + origins.Count) % origins.Count
				})
				.OrderBy(candidate => candidate.distance)
				.ThenBy(candidate => candidate.tieBreak)
				.ThenBy(candidate => candidate.Galaxy)
				.ThenBy(candidate => candidate.System)
				.Select(candidate => new DiscoveryOriginSystemPair(
					candidate.originIndex,
					candidate.Galaxy,
					candidate.System,
					candidate.distance))
				.FirstOrDefault();
		}

		public static int CircularDistance(int firstSystem, int secondSystem, int maxSystem) {
			if (maxSystem <= 0)
				return int.MaxValue;
			var first = NormalizeSystem(firstSystem, maxSystem);
			var second = NormalizeSystem(secondSystem, maxSystem);
			var direct = Math.Abs(first - second);
			return Math.Min(direct, maxSystem - direct);
		}

		private static string Key(int galaxy, int system) => $"{galaxy}:{system}";

		private static int NormalizeIndex(int index, int count) {
			var normalized = index % count;
			return normalized < 0 ? normalized + count : normalized;
		}

		private static int NormalizeSystem(int system, int maxSystem) {
			if (system < 1)
				return 1;
			return ((system - 1) % maxSystem) + 1;
		}
	}
}
