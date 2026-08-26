namespace Icod.Host.Tests;

using Icod.Host;
using Xunit;

/// <summary>Tests deterministic processor-list, affinity, and quota parsers.</summary>
public sealed class HostResourceParserTests {
	/// <summary>Verifies Linux CPU-list ranges are expanded and deduplicated.</summary>
	[Fact]
	public void ProcessorListExpandsRanges() {
		Assert.Equal(
			new[] { 0, 1, 2, 3, 8, 10, 11 },
			HostResourceParsers.ParseProcessorList( "0-3,8,10-11,3" )
		);
	}

	/// <summary>Verifies malformed descending ranges are rejected.</summary>
	[Fact]
	public void ProcessorListRejectsDescendingRange() {
		Assert.Throws<FormatException>(
			() => HostResourceParsers.ParseProcessorList( "4-2" )
		);
	}

	/// <summary>Verifies affinity mask bit counting and index extraction.</summary>
	[Fact]
	public void AffinityMaskReportsSelectedProcessors() {
		var mask = new byte[] { 0b1000_0101, 0b0000_0010 };

		Assert.Equal( 4, HostResourceParsers.CountSetBits( mask ) );
		Assert.Equal( new[] { 0, 2, 7, 9 }, HostResourceParsers.GetSetBitIndices( mask ) );
	}

	/// <summary>Verifies affinity descriptors retain identifier namespaces and normalize values.</summary>
	[Fact]
	public void AffinityDescriptorNormalizesIdentifiers() {
		var descriptor = new ProcessorAffinityDescriptor(
			new long[] { 9, 2, 9 },
			isComplete: true,
			identifierKind: ProcessorSelectionIdentifierKind.WindowsCpuSetId
		);

		Assert.Equal( new long[] { 2, 9 }, descriptor.ProcessorIdentifiers );
		Assert.Equal( ProcessorSelectionIdentifierKind.WindowsCpuSetId, descriptor.IdentifierKind );
		Assert.Equal( 2, descriptor.Count );
	}

	/// <summary>Verifies empty affinity selections are rejected instead of reported as available.</summary>
	[Fact]
	public void AffinityDescriptorRejectsEmptySelection() {
		Assert.Throws<ArgumentException>(
			() => new ProcessorAffinityDescriptor( Array.Empty<long>(), isComplete: true )
		);
	}

	/// <summary>Verifies quota intervals must be supplied as a complete pair.</summary>
	[Fact]
	public void QuotaDescriptorRejectsPartialInterval() {
		Assert.Throws<ArgumentException>(
			() => new ProcessorQuotaDescriptor( 1, 100000, null, "test" )
		);
	}

	/// <summary>Verifies cgroup v2 quota conversion preserves fractional capacity.</summary>
	[Fact]
	public void ControlGroupV2QuotaIsFractionalProcessorCapacity() {
		var result = HostResourceParsers.ParseControlGroupV2CpuMax( "150000 100000" );

		Assert.True( result.IsAvailable );
		Assert.Equal( 1.5, result.GetRequiredValue().ProcessorLimit, 8 );
		Assert.Equal( HostResourceProvenance.LinuxControlGroupV2, result.Provenance );
	}

	/// <summary>Verifies an unlimited cgroup v2 controller is not a zero quota.</summary>
	[Fact]
	public void ControlGroupV2UnlimitedIsNotApplicable() {
		var result = HostResourceParsers.ParseControlGroupV2CpuMax( "max 100000" );

		Assert.Equal( HostResourceAvailability.NotApplicable, result.Availability );
	}

	/// <summary>Verifies cgroup v1 negative quota denotes no hard limit.</summary>
	[Fact]
	public void ControlGroupV1NegativeQuotaIsNotApplicable() {
		var result = HostResourceParsers.ParseControlGroupV1CpuQuota( "-1", "100000" );

		Assert.Equal( HostResourceAvailability.NotApplicable, result.Availability );
	}
}
