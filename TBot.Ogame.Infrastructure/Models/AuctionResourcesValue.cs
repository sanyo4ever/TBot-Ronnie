using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TBot.Ogame.Infrastructure.Models {
	public class AuctionResourcesValue {
		public string imageFileName { get; set; }
		public AuctionInputOutput input { get; set; }
		public AuctionInputOutput output { get; set; }
		public bool isMoon { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }

		public int? otherPlanetId { get; set; } = 0;

		public override string ToString() {
			string outStr = "[";
			if (isMoon)
				outStr += "M";
			else
				outStr += "P";
			outStr +=
				$" \"{Name}\" {otherPlanetId} " +
				$"Input: M:{input?.Metal ?? 0} C:{input?.Crystal ?? 0} D:{input?.Deuterium ?? 0} " +
				$"Output: M:{output?.Metal ?? 0} C:{output?.Crystal ?? 0} D:{output?.Deuterium ?? 0}]";
			return outStr;
		}
	}

}
