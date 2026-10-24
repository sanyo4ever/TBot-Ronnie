using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	/// <summary>
	/// Celestial under consideration to be targetted for farming.
	/// </summary>
	public class FarmTarget {
		public FarmTarget(Celestial target, FarmState farmState = FarmState.Idle, EspionageReport report = null) {
			Celestial = target;
			State = farmState;
			Report = report;
		}
		public Celestial Celestial { get; set; }
		public FarmState State { get; set; }
		public EspionageReport Report { get; set; }
		public int? ConsumedReportId { get; set; }
		/// <summary>
		/// ID of the AutoFarm espionage fleet currently collecting this target's
		/// report. It is persisted so a worker restart cannot make an in-flight
		/// probe look like an available target again.
		/// </summary>
		public int? ProbeFleetId { get; set; }
		/// <summary>
		/// UTC time at which the current AutoFarm probe was dispatched. It gives
		/// the worker a short recovery window when OGame temporarily omits a
		/// returning fleet from its fleet snapshot.
		/// </summary>
		public DateTime? ProbeSentAtUtc { get; set; }
		/// <summary>
		/// Probe round used to obtain enough report information. Zero is the
		/// initial probe, one requests the second round, and two the final round.
		/// </summary>
		public int ProbeLevel { get; set; }
		public bool HasCoords(Coordinate coords) {
			return coords != null && Celestial?.Coordinate != null
				&& coords.Galaxy == Celestial.Coordinate.Galaxy
				&& coords.System == Celestial.Coordinate.System
				&& coords.Position == Celestial.Coordinate.Position
				&& coords.Type == Celestial.Coordinate.Type;
		}
		private string GetCelestialCode() {
			return Celestial.Coordinate.Type switch {
				Celestials.Planet => "P",
				Celestials.Debris => "DF",
				Celestials.Moon => "M",
				Celestials.DeepSpace => "DS",
				_ => "",
			};
		}
		public override string ToString() {
			return $"[{GetCelestialCode()}:{Celestial.Coordinate.Galaxy}:{Celestial.Coordinate.System}:{Celestial.Coordinate.Position}]";
		}
	}

}
