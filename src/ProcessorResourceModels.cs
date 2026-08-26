namespace Icod.Host;

/// <summary>
/// Identifies a host or process capability exposed by the processor provider.
/// </summary>
public enum HostResourceCapabilityKind {
	/// <summary>Native or stable host identifier retrieval.</summary>
	HostIdentifier = 0,
	/// <summary>Configured processor count.</summary>
	ConfiguredProcessorCount = 1,
	/// <summary>Installed or present processor count.</summary>
	InstalledProcessorCount = 2,
	/// <summary>Online processor count.</summary>
	OnlineProcessorCount = 3,
	/// <summary>Processors available to the current process.</summary>
	ProcessAvailableProcessorCount = 4,
	/// <summary>Current-process affinity or processor-set inspection.</summary>
	ProcessAffinity = 5,
	/// <summary>Container, cgroup, or job-object quota inspection.</summary>
	ProcessorQuota = 6,
	/// <summary>Processor package and core topology.</summary>
	ProcessorTopology = 7,
	/// <summary>NUMA topology.</summary>
	NumaTopology = 8
}

/// <summary>
/// Summarizes one capability and the provider state observed for it.
/// </summary>
public sealed record HostResourceCapability {
	/// <summary>Initializes a capability report.</summary>
	/// <param name="kind">The capability kind.</param>
	/// <param name="availability">The observed availability.</param>
	/// <param name="provenance">The observation source.</param>
	/// <param name="message">An optional explanation.</param>
	public HostResourceCapability(
		HostResourceCapabilityKind kind,
		HostResourceAvailability availability,
		HostResourceProvenance provenance,
		string? message = null
	) {
		Kind = kind;
		Availability = availability;
		Provenance = provenance;
		Message = message;
	}

	/// <summary>Gets the capability kind.</summary>
	public HostResourceCapabilityKind Kind { get; }

	/// <summary>Gets the capability availability.</summary>
	public HostResourceAvailability Availability { get; }

	/// <summary>Gets the source of the capability observation.</summary>
	public HostResourceProvenance Provenance { get; }

	/// <summary>Gets an optional explanation.</summary>
	public string? Message { get; }
}

/// <summary>
/// Identifies the namespace used by processor-selection identifiers.
/// </summary>
public enum ProcessorSelectionIdentifierKind {
	/// <summary>The identifiers are zero-based logical-processor indices.</summary>
	LogicalProcessorIndex = 0,
	/// <summary>The identifiers are opaque Windows CPU-set identifiers.</summary>
	WindowsCpuSetId = 1
}

/// <summary>
/// Describes the processors selected for the current process.
/// </summary>
public sealed record ProcessorAffinityDescriptor {
	/// <summary>Initializes an affinity descriptor.</summary>
	/// <param name="processorIdentifiers">The selected processor identifiers.</param>
	/// <param name="isComplete">Whether the list covers every processor group or equivalent host domain.</param>
	/// <param name="identifierKind">The namespace used by the identifiers.</param>
	/// <exception cref="ArgumentNullException"><paramref name="processorIdentifiers"/> is null.</exception>
	/// <exception cref="ArgumentException">No processor identifier was supplied.</exception>
	/// <exception cref="ArgumentOutOfRangeException">A processor identifier is negative.</exception>
	public ProcessorAffinityDescriptor(
		IEnumerable<long> processorIdentifiers,
		bool isComplete,
		ProcessorSelectionIdentifierKind identifierKind = ProcessorSelectionIdentifierKind.LogicalProcessorIndex
	) {
		ArgumentNullException.ThrowIfNull( processorIdentifiers );
		var identifiers = processorIdentifiers.Distinct().Order().ToArray();
		if ( identifiers.Length == 0 ) {
			throw new ArgumentException(
				"An affinity descriptor requires at least one processor identifier.",
				nameof( processorIdentifiers )
			);
		}
		if ( identifiers[0] < 0 ) {
			throw new ArgumentOutOfRangeException(
				nameof( processorIdentifiers ),
				"Processor identifiers cannot be negative."
			);
		}
		ProcessorIdentifiers = Array.AsReadOnly( identifiers );
		IsComplete = isComplete;
		IdentifierKind = identifierKind;
	}

	/// <summary>Gets the selected processor identifiers.</summary>
	public IReadOnlyList<long> ProcessorIdentifiers { get; }

	/// <summary>Gets the namespace used by the processor identifiers.</summary>
	public ProcessorSelectionIdentifierKind IdentifierKind { get; }

	/// <summary>Gets the number of selected processors.</summary>
	public int Count => ProcessorIdentifiers.Count;

	/// <summary>Gets whether the list covers every processor group or equivalent host domain.</summary>
	public bool IsComplete { get; }
}

/// <summary>
/// Describes a hard processor-time quota as a fractional processor capacity.
/// </summary>
public sealed record ProcessorQuotaDescriptor {
	/// <summary>Initializes a processor quota.</summary>
	/// <param name="processorLimit">The fractional processor capacity made available.</param>
	/// <param name="quotaMicroseconds">The optional quota interval in microseconds.</param>
	/// <param name="periodMicroseconds">The optional accounting period in microseconds.</param>
	/// <param name="scope">A short description of the quota scope.</param>
	/// <exception cref="ArgumentOutOfRangeException">The processor limit, quota, or period is not positive.</exception>
	/// <exception cref="ArgumentException">
	/// Only one of quota and period is present, or <paramref name="scope"/> is empty.
	/// </exception>
	public ProcessorQuotaDescriptor(
		double processorLimit,
		long? quotaMicroseconds,
		long? periodMicroseconds,
		string scope
	) {
		if ( !double.IsFinite( processorLimit ) || processorLimit <= 0 ) {
			throw new ArgumentOutOfRangeException( nameof( processorLimit ) );
		}
		if ( quotaMicroseconds.HasValue != periodMicroseconds.HasValue ) {
			throw new ArgumentException(
				"Quota and period values must either both be present or both be absent.",
				nameof( quotaMicroseconds )
			);
		}
		if (
			quotaMicroseconds.HasValue
			&& (quotaMicroseconds.Value <= 0 || periodMicroseconds.GetValueOrDefault() <= 0)
		) {
			throw new ArgumentOutOfRangeException(
				nameof( quotaMicroseconds ),
				"Quota and period values must be positive."
			);
		}
		if ( string.IsNullOrWhiteSpace( scope ) ) {
			throw new ArgumentException( "A processor quota requires a nonempty scope.", nameof( scope ) );
		}
		ProcessorLimit = processorLimit;
		QuotaMicroseconds = quotaMicroseconds;
		PeriodMicroseconds = periodMicroseconds;
		Scope = scope;
	}

	/// <summary>Gets the fractional processor capacity.</summary>
	public double ProcessorLimit { get; }

	/// <summary>Gets the optional quota interval in microseconds.</summary>
	public long? QuotaMicroseconds { get; }

	/// <summary>Gets the optional accounting period in microseconds.</summary>
	public long? PeriodMicroseconds { get; }

	/// <summary>Gets a short description of the quota scope.</summary>
	public string Scope { get; }
}

/// <summary>
/// Describes optional processor package, core, logical-processor, and NUMA facts.
/// </summary>
public sealed record ProcessorTopologyDescriptor {
	/// <summary>Initializes a topology descriptor.</summary>
	/// <param name="packages">The processor-package count.</param>
	/// <param name="physicalCores">The physical-core count.</param>
	/// <param name="logicalProcessors">The logical-processor count.</param>
	/// <param name="numaNodes">The NUMA-node count.</param>
	public ProcessorTopologyDescriptor(
		HostResourceValue<int> packages,
		HostResourceValue<int> physicalCores,
		HostResourceValue<int> logicalProcessors,
		HostResourceValue<int> numaNodes
	) {
		Packages = packages;
		PhysicalCores = physicalCores;
		LogicalProcessors = logicalProcessors;
		NumaNodes = numaNodes;
	}

	/// <summary>Gets the processor-package count.</summary>
	public HostResourceValue<int> Packages { get; }

	/// <summary>Gets the physical-core count.</summary>
	public HostResourceValue<int> PhysicalCores { get; }

	/// <summary>Gets the logical-processor count.</summary>
	public HostResourceValue<int> LogicalProcessors { get; }

	/// <summary>Gets the NUMA-node count.</summary>
	public HostResourceValue<int> NumaNodes { get; }
}

/// <summary>
/// Collects processor-resource facts for the host and current process.
/// </summary>
public sealed record ProcessorResourceSnapshot {
	/// <summary>Initializes a processor-resource snapshot.</summary>
	/// <param name="configuredProcessorCount">The configured processor count.</param>
	/// <param name="installedProcessorCount">The installed or present processor count.</param>
	/// <param name="onlineProcessorCount">The online processor count.</param>
	/// <param name="processAvailableProcessorCount">The runtime-observed process-available count.</param>
	/// <param name="affinity">The current-process affinity or processor-set observation.</param>
	/// <param name="quota">The current container, cgroup, or job-object quota.</param>
	/// <param name="topology">The optional package, core, logical, and NUMA topology.</param>
	public ProcessorResourceSnapshot(
		HostResourceValue<int> configuredProcessorCount,
		HostResourceValue<int> installedProcessorCount,
		HostResourceValue<int> onlineProcessorCount,
		HostResourceValue<int> processAvailableProcessorCount,
		HostResourceValue<ProcessorAffinityDescriptor> affinity,
		HostResourceValue<ProcessorQuotaDescriptor> quota,
		HostResourceValue<ProcessorTopologyDescriptor> topology
	) {
		ConfiguredProcessorCount = configuredProcessorCount;
		InstalledProcessorCount = installedProcessorCount;
		OnlineProcessorCount = onlineProcessorCount;
		ProcessAvailableProcessorCount = processAvailableProcessorCount;
		Affinity = affinity;
		Quota = quota;
		Topology = topology;
		Capabilities = BuildCapabilities();
	}

	/// <summary>Gets the configured processor count.</summary>
	public HostResourceValue<int> ConfiguredProcessorCount { get; }

	/// <summary>Gets the installed or present processor count.</summary>
	public HostResourceValue<int> InstalledProcessorCount { get; }

	/// <summary>Gets the online processor count.</summary>
	public HostResourceValue<int> OnlineProcessorCount { get; }

	/// <summary>Gets the processors available to the current process according to the runtime.</summary>
	public HostResourceValue<int> ProcessAvailableProcessorCount { get; }

	/// <summary>Gets the current-process affinity or processor-set observation.</summary>
	public HostResourceValue<ProcessorAffinityDescriptor> Affinity { get; }

	/// <summary>Gets the current container, cgroup, or job-object processor quota.</summary>
	public HostResourceValue<ProcessorQuotaDescriptor> Quota { get; }

	/// <summary>Gets the optional processor topology.</summary>
	public HostResourceValue<ProcessorTopologyDescriptor> Topology { get; }

	/// <summary>Gets a capability report derived from the individual observations.</summary>
	public IReadOnlyList<HostResourceCapability> Capabilities { get; }

	private IReadOnlyList<HostResourceCapability> BuildCapabilities() {
		var topologyAvailability = Topology.Availability;
		var numaAvailability = topologyAvailability;
		var numaProvenance = Topology.Provenance;
		var numaMessage = Topology.Message;
		if ( Topology.IsAvailable ) {
			var descriptor = Topology.GetRequiredValue();
			numaAvailability = descriptor.NumaNodes.Availability;
			numaProvenance = descriptor.NumaNodes.Provenance;
			numaMessage = descriptor.NumaNodes.Message;
		}

		return [
			CreateCapability( HostResourceCapabilityKind.ConfiguredProcessorCount, ConfiguredProcessorCount ),
			CreateCapability( HostResourceCapabilityKind.InstalledProcessorCount, InstalledProcessorCount ),
			CreateCapability( HostResourceCapabilityKind.OnlineProcessorCount, OnlineProcessorCount ),
			CreateCapability(
				HostResourceCapabilityKind.ProcessAvailableProcessorCount,
				ProcessAvailableProcessorCount
			),
			CreateCapability( HostResourceCapabilityKind.ProcessAffinity, Affinity ),
			CreateCapability( HostResourceCapabilityKind.ProcessorQuota, Quota ),
			CreateCapability( HostResourceCapabilityKind.ProcessorTopology, Topology ),
			new HostResourceCapability(
				HostResourceCapabilityKind.NumaTopology,
				numaAvailability,
				numaProvenance,
				numaMessage
			)
		];
	}

	private static HostResourceCapability CreateCapability<T>(
		HostResourceCapabilityKind kind,
		HostResourceValue<T> value
	) => new(
		kind,
		value.Availability,
		value.Provenance,
		value.Message
	);
}

/// <summary>
/// Collects the host identifier and processor-resource observations made at one
/// point in time.
/// </summary>
public sealed record HostResourceSnapshot {
	/// <summary>Initializes a combined host-resource snapshot.</summary>
	/// <param name="hostIdentifier">The host-identifier observation.</param>
	/// <param name="processors">The processor-resource observations.</param>
	/// <param name="observedAtUtc">The observation timestamp.</param>
	/// <exception cref="ArgumentNullException"><paramref name="processors"/> is null.</exception>
	public HostResourceSnapshot(
		HostResourceValue<HostIdentifier> hostIdentifier,
		ProcessorResourceSnapshot processors,
		DateTimeOffset observedAtUtc
	) {
		HostIdentifier = hostIdentifier;
		Processors = processors ?? throw new ArgumentNullException( nameof( processors ) );
		ObservedAtUtc = observedAtUtc;
	}

	/// <summary>Gets the host-identifier observation.</summary>
	public HostResourceValue<HostIdentifier> HostIdentifier { get; }

	/// <summary>Gets the processor-resource observations.</summary>
	public ProcessorResourceSnapshot Processors { get; }

	/// <summary>Gets the UTC observation timestamp.</summary>
	public DateTimeOffset ObservedAtUtc { get; }

	/// <summary>Gets the complete capability report, including host-identifier support.</summary>
	public IReadOnlyList<HostResourceCapability> Capabilities => [
		new HostResourceCapability(
			HostResourceCapabilityKind.HostIdentifier,
			HostIdentifier.Availability,
			HostIdentifier.Provenance,
			HostIdentifier.Message
		),
		.. Processors.Capabilities
	];
}
