namespace Icod.Host.Tests;

using Icod.Host;
using Xunit;

/// <summary>Tests explicit host-resource availability and provenance.</summary>
public sealed class HostResourceValueTests {
	/// <summary>Verifies that available values retain their source.</summary>
	[Fact]
	public void AvailableValueRetainsProvenance() {
		var value = HostResourceValue<int>.Available(
			8,
			HostResourceProvenance.ManagedRuntime
		);

		Assert.True( value.IsAvailable );
		Assert.Equal( 8, value.GetRequiredValue() );
		Assert.Equal( HostResourceProvenance.ManagedRuntime, value.Provenance );
	}

	/// <summary>Verifies that unavailable values cannot masquerade as zero.</summary>
	[Fact]
	public void UnavailableValueRejectsRequiredAccess() {
		var value = HostResourceValue<int>.Unsupported( "not supported" );

		Assert.False( value.IsAvailable );
		Assert.Equal( HostResourceAvailability.Unsupported, value.Availability );
		Assert.Throws<InvalidOperationException>( () => { _ = value.GetRequiredValue(); } );
	}
}
