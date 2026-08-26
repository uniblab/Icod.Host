namespace Icod.Host.Tests;

using Icod.Host;
using Xunit;

/// <summary>Tests the injectable and system host-resource provider boundaries.</summary>
public sealed class SystemHostResourceProviderTests {
	/// <summary>Verifies an injected provider can supply deterministic snapshots.</summary>
	[Fact]
	public async Task ProviderContractIsInjectable() {
		IHostResourceProvider provider = new FixedProvider();

		var snapshot = await provider.ObserveAsync();

		Assert.Equal( "01020304", snapshot.HostIdentifier.GetRequiredValue().Hexadecimal );
		Assert.Equal( 12, snapshot.Processors.ConfiguredProcessorCount.GetRequiredValue() );
		Assert.Equal( 4, snapshot.Processors.ProcessAvailableProcessorCount.GetRequiredValue() );
		Assert.Contains(
			snapshot.Capabilities,
			static capability => capability.Kind == HostResourceCapabilityKind.ProcessAffinity
		);
	}

	/// <summary>Verifies the system provider returns controlled, internally consistent observations.</summary>
	[Fact]
	public async Task SystemProviderReturnsControlledSnapshot() {
		var snapshot = await SystemHostResourceProvider.Instance.ObserveAsync();

		Assert.True( snapshot.Processors.ProcessAvailableProcessorCount.IsAvailable );
		Assert.True( snapshot.Processors.ProcessAvailableProcessorCount.GetRequiredValue() >= 1 );
		Assert.Equal( 9, snapshot.Capabilities.Count );
		Assert.All(
			snapshot.Capabilities,
			static capability => Assert.True( Enum.IsDefined( capability.Availability ) )
		);
		if ( snapshot.Processors.OnlineProcessorCount.IsAvailable ) {
			Assert.True( snapshot.Processors.OnlineProcessorCount.GetRequiredValue() >= 1 );
		}
		if ( snapshot.Processors.Affinity.IsAvailable ) {
			Assert.True( snapshot.Processors.Affinity.GetRequiredValue().Count >= 1 );
		}
	}

	private sealed class FixedProvider : IHostResourceProvider {
		/// <inheritdoc />
		public ValueTask<HostResourceValue<HostIdentifier>> GetHostIdentifierAsync(
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			return ValueTask.FromResult(
				HostResourceValue<HostIdentifier>.Available(
					new HostIdentifier( 0x01020304, "test" ),
					HostResourceProvenance.Derived
				)
			);
		}

		/// <inheritdoc />
		public ValueTask<ProcessorResourceSnapshot> GetProcessorResourcesAsync(
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			return ValueTask.FromResult( CreateProcessors() );
		}

		/// <inheritdoc />
		public async ValueTask<HostResourceSnapshot> ObserveAsync(
			CancellationToken cancellationToken = default
		) {
			var host = await GetHostIdentifierAsync( cancellationToken );
			var processors = await GetProcessorResourcesAsync( cancellationToken );
			return new HostResourceSnapshot(
				host,
				processors,
				DateTimeOffset.UnixEpoch
			);
		}

		private static ProcessorResourceSnapshot CreateProcessors() {
			var configured = HostResourceValue<int>.Available( 12, HostResourceProvenance.Derived );
			var installed = HostResourceValue<int>.Available( 8, HostResourceProvenance.Derived );
			var online = HostResourceValue<int>.Available( 6, HostResourceProvenance.Derived );
			var available = HostResourceValue<int>.Available( 4, HostResourceProvenance.Derived );
			var affinity = HostResourceValue<ProcessorAffinityDescriptor>.Available(
				new ProcessorAffinityDescriptor( new long[] { 0, 1, 2, 3 }, true ),
				HostResourceProvenance.Derived
			);
			var quota = HostResourceValue<ProcessorQuotaDescriptor>.Available(
				new ProcessorQuotaDescriptor( 2.5, 250000, 100000, "test" ),
				HostResourceProvenance.Derived
			);
			var topology = HostResourceValue<ProcessorTopologyDescriptor>.Available(
				new ProcessorTopologyDescriptor(
					HostResourceValue<int>.Available( 1, HostResourceProvenance.Derived ),
					HostResourceValue<int>.Available( 4, HostResourceProvenance.Derived ),
					installed,
					HostResourceValue<int>.Available( 1, HostResourceProvenance.Derived )
				),
				HostResourceProvenance.Derived
			);
			return new ProcessorResourceSnapshot(
				configured,
				installed,
				online,
				available,
				affinity,
				quota,
				topology
			);
		}
	}
}
