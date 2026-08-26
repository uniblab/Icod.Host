namespace Icod.Host;

using System.Globalization;
using System.Numerics;

/// <summary>
/// Provides deterministic parsers used by platform host-resource providers and
/// their tests.
/// </summary>
public static class HostResourceParsers {
	private const int MaximumLogicalProcessorId = 1_048_575;

	/// <summary>
	/// Parses a Linux CPU-list expression such as <c>0-3,8,10-11</c>.
	/// </summary>
	/// <param name="text">The CPU-list text.</param>
	/// <returns>The sorted, distinct logical processor identifiers.</returns>
	/// <exception cref="FormatException">The expression is malformed.</exception>
	public static IReadOnlyList<int> ParseProcessorList( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		var result = new SortedSet<int>();
		foreach ( var untrimmedSegment in text.Split( ',', StringSplitOptions.RemoveEmptyEntries ) ) {
			var segment = untrimmedSegment.Trim();
			if ( segment.Length == 0 ) {
				continue;
			}
			var dash = segment.IndexOf( '-' );
			if ( dash < 0 ) {
				result.Add( ParseProcessorId( segment ) );
				continue;
			}
			if ( dash == 0 || dash == segment.Length - 1 || segment.IndexOf( '-', dash + 1 ) >= 0 ) {
				throw new FormatException( "The processor-list range is malformed." );
			}
			var start = ParseProcessorId( segment[..dash] );
			var end = ParseProcessorId( segment[(dash + 1)..] );
			if ( end < start ) {
				throw new FormatException( "The processor-list range ends before it begins." );
			}
			for ( var processor = start; processor <= end; processor++ ) {
				result.Add( processor );
			}
		}
		if ( result.Count == 0 ) {
			throw new FormatException( "The processor list does not contain a processor identifier." );
		}
		return result.ToArray();
	}

	/// <summary>Counts selected bits in a native affinity mask.</summary>
	/// <param name="mask">The affinity-mask bytes.</param>
	/// <returns>The number of selected bits.</returns>
	public static int CountSetBits( ReadOnlySpan<byte> mask ) {
		var count = 0;
		foreach ( var value in mask ) {
			count += BitOperations.PopCount( value );
		}
		return count;
	}

	/// <summary>Returns logical processor identifiers selected by a native affinity mask.</summary>
	/// <param name="mask">The affinity-mask bytes, ordered least-significant byte first.</param>
	/// <returns>The selected logical processor identifiers.</returns>
	public static IReadOnlyList<int> GetSetBitIndices( ReadOnlySpan<byte> mask ) {
		var result = new List<int>();
		for ( var byteIndex = 0; byteIndex < mask.Length; byteIndex++ ) {
			for ( var bit = 0; bit < 8; bit++ ) {
				if ( 0 != (mask[byteIndex] & (1 << bit)) ) {
					result.Add( checked((byteIndex * 8) + bit) );
				}
			}
		}
		return result;
	}

	/// <summary>Parses the cgroup v2 <c>cpu.max</c> format.</summary>
	/// <param name="text">The file contents.</param>
	/// <returns>An available hard quota or a not-applicable unlimited result.</returns>
	public static HostResourceValue<ProcessorQuotaDescriptor> ParseControlGroupV2CpuMax( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		var fields = text.Split( (char[]?)null, StringSplitOptions.RemoveEmptyEntries );
		if ( fields.Length != 2 ) {
			return HostResourceValue<ProcessorQuotaDescriptor>.Unavailable(
				"The cgroup v2 cpu.max value is malformed.",
				HostResourceProvenance.LinuxControlGroupV2
			);
		}
		if ( fields[0].Equals( "max", StringComparison.Ordinal ) ) {
			return HostResourceValue<ProcessorQuotaDescriptor>.NotApplicable(
				"The cgroup v2 CPU controller does not impose a hard quota.",
				HostResourceProvenance.LinuxControlGroupV2
			);
		}
		if (
			!long.TryParse( fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var quota )
			|| !long.TryParse( fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var period )
			|| quota <= 0
			|| period <= 0
		) {
			return HostResourceValue<ProcessorQuotaDescriptor>.Unavailable(
				"The cgroup v2 CPU quota or period is invalid.",
				HostResourceProvenance.LinuxControlGroupV2
			);
		}
		return HostResourceValue<ProcessorQuotaDescriptor>.Available(
			new ProcessorQuotaDescriptor(
				(double)quota / period,
				quota,
				period,
				"cgroup v2"
			),
			HostResourceProvenance.LinuxControlGroupV2
		);
	}

	/// <summary>Parses the cgroup v1 CPU quota and period files.</summary>
	/// <param name="quotaText">The <c>cpu.cfs_quota_us</c> contents.</param>
	/// <param name="periodText">The <c>cpu.cfs_period_us</c> contents.</param>
	/// <returns>An available hard quota or a not-applicable unlimited result.</returns>
	public static HostResourceValue<ProcessorQuotaDescriptor> ParseControlGroupV1CpuQuota(
		string quotaText,
		string periodText
	) {
		ArgumentNullException.ThrowIfNull( quotaText );
		ArgumentNullException.ThrowIfNull( periodText );
		if (
			!long.TryParse( quotaText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quota )
			|| !long.TryParse( periodText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var period )
			|| period <= 0
		) {
			return HostResourceValue<ProcessorQuotaDescriptor>.Unavailable(
				"The cgroup v1 CPU quota or period is invalid.",
				HostResourceProvenance.LinuxControlGroupV1
			);
		}
		if ( quota < 0 ) {
			return HostResourceValue<ProcessorQuotaDescriptor>.NotApplicable(
				"The cgroup v1 CPU controller does not impose a hard quota.",
				HostResourceProvenance.LinuxControlGroupV1
			);
		}
		if ( quota == 0 ) {
			return HostResourceValue<ProcessorQuotaDescriptor>.Unavailable(
				"The cgroup v1 CPU quota is zero.",
				HostResourceProvenance.LinuxControlGroupV1
			);
		}
		return HostResourceValue<ProcessorQuotaDescriptor>.Available(
			new ProcessorQuotaDescriptor(
				(double)quota / period,
				quota,
				period,
				"cgroup v1"
			),
			HostResourceProvenance.LinuxControlGroupV1
		);
	}

	private static int ParseProcessorId( string text ) {
		if (
			!int.TryParse( text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value )
			|| value < 0
			|| value > MaximumLogicalProcessorId
		) {
			throw new FormatException( "The processor identifier is invalid." );
		}
		return value;
	}
}
