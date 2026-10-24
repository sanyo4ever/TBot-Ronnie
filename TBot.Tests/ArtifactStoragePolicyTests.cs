using TBot.Model;
using Xunit;

namespace TBot.Tests {
	public class ArtifactStoragePolicyTests {
		[Fact]
		public void ArtifactPauseUsesHysteresisInsteadOfTogglingAtOneThreshold() {
			Assert.Equal(ArtifactStorageState.Paused,
				ArtifactStoragePolicy.NextState(ArtifactStorageState.Active, 3600, 3600, 3000));
			Assert.Equal(ArtifactStorageState.Paused,
				ArtifactStoragePolicy.NextState(ArtifactStorageState.Paused, 3500, 3600, 3000));
			Assert.Equal(ArtifactStorageState.Active,
				ArtifactStoragePolicy.NextState(ArtifactStorageState.Paused, 3000, 3600, 3000));
			Assert.Equal(ArtifactStorageState.Active,
				ArtifactStoragePolicy.NextState(ArtifactStorageState.Active, 3500, 3600, 3000));
		}
	}
}
