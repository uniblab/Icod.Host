namespace Icod.Host;

using Microsoft.Win32;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

/// <summary>
/// Reads host identifiers and processor-resource facts from managed and narrow
/// native operating-system providers.
/// </summary>
public sealed class SystemHostResourceProvider : IHostResourceProvider {
	private const int LinuxConfiguredProcessors = 83;
	private const int LinuxOnlineProcessors = 84;
	private const ushort AllProcessorGroups = ushort.MaxValue;
	private const uint JobObjectCpuRateControlEnable = 0x1;
	private const uint JobObjectCpuRateControlWeightBased = 0x2;
	private const uint JobObjectCpuRateControlHardCap = 0x4;
	private const uint JobObjectCpuRateControlMinMaxRate = 0x10;
	private const int JobObjectCpuRateControlInformation = 15;

	/// <summary>Gets the process-wide system provider.</summary>
	public static SystemHostResourceProvider Instance { get; } = new();

	private SystemHostResourceProvider() { }

	/// <inheritdoc />
	public ValueTask<HostResourceValue<HostIdentifier>> GetHostIdentifierAsync(
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult( ObserveHostIdentifier() );
	}

	/// <inheritdoc />
	public ValueTask<ProcessorResourceSnapshot> GetProcessorResourcesAsync(
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult( ObserveProcessorResources() );
	}

	/// <inheritdoc />
	public ValueTask<HostResourceSnapshot> ObserveAsync(
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		var hostIdentifier = ObserveHostIdentifier();
		cancellationToken.ThrowIfCancellationRequested();
		var processors = ObserveProcessorResources();
		return ValueTask.FromResult(
			new HostResourceSnapshot(
				hostIdentifier,
				processors,
				DateTimeOffset.UtcNow
			)
		);
	}

	private static HostResourceValue<HostIdentifier> ObserveHostIdentifier() {
		if ( OperatingSystem.IsWindows() ) {
			var registryIdentifier = TryReadWindowsMachineGuid();
			if ( registryIdentifier.IsAvailable ) {
				return registryIdentifier;
			}
		} else {
			try {
				var nativeValue = NativeMethods.GetHostId();
				return HostResourceValue<HostIdentifier>.Available(
					new HostIdentifier(
						HostIdentifierNormalizer.NormalizeNative( nativeValue.ToInt64() ),
						"native gethostid"
					),
					HostResourceProvenance.NativeOperatingSystem
				);
			} catch ( DllNotFoundException ) {
				// Continue to stable textual identifiers.
			} catch ( EntryPointNotFoundException ) {
				// Continue to stable textual identifiers.
			} catch ( PlatformNotSupportedException ) {
				// Continue to stable textual identifiers.
			}
		}

		foreach ( var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" } ) {
			if ( TryReadNonEmptyText( path, out var machineIdentifier ) ) {
				return HostResourceValue<HostIdentifier>.Available(
					new HostIdentifier(
						HostIdentifierNormalizer.NormalizeStableText( machineIdentifier ),
						"stable machine identifier"
					),
					HostResourceProvenance.Derived,
					"A stable textual machine identifier was deterministically folded to 32 bits."
				);
			}
		}

		try {
			var hostName = Dns.GetHostName();
			if ( hostName.Length > 0 ) {
				return HostResourceValue<HostIdentifier>.Available(
					new HostIdentifier(
						HostIdentifierNormalizer.NormalizeStableText( hostName ),
						"host name fallback"
					),
					HostResourceProvenance.Derived,
					"No native or stable machine identifier was available; the normalized host name was used."
				);
			}
		} catch ( Exception ex ) {
			return HostResourceValue<HostIdentifier>.Unavailable(
				ex.Message,
				HostResourceProvenance.Derived
			);
		}
		return HostResourceValue<HostIdentifier>.Unavailable(
			"The host did not expose a native, stable-machine, or host-name identifier."
		);
	}

	[SupportedOSPlatform( "windows" )]
	private static HostResourceValue<HostIdentifier> TryReadWindowsMachineGuid() {
		try {
			using var key = Registry.LocalMachine.OpenSubKey( @"SOFTWARE\Microsoft\Cryptography", writable: false );
			var value = key?.GetValue( "MachineGuid" ) as string;
			if ( string.IsNullOrWhiteSpace( value ) ) {
				return HostResourceValue<HostIdentifier>.Unavailable(
					"The Windows MachineGuid registry value is unavailable.",
					HostResourceProvenance.WindowsRegistry
				);
			}
			return HostResourceValue<HostIdentifier>.Available(
				new HostIdentifier(
					HostIdentifierNormalizer.NormalizeStableText( value ),
					"Windows MachineGuid"
				),
				HostResourceProvenance.WindowsRegistry,
				"The stable MachineGuid was deterministically folded to 32 bits."
			);
		} catch ( Exception ex ) {
			return HostResourceValue<HostIdentifier>.Unavailable(
				ex.Message,
				HostResourceProvenance.WindowsRegistry
			);
		}
	}

	private static ProcessorResourceSnapshot ObserveProcessorResources() {
		var processAvailable = HostResourceValue<int>.Available(
			Math.Max( 1, Environment.ProcessorCount ),
			HostResourceProvenance.ManagedRuntime,
			"The managed runtime applies host affinity and container restrictions where supported."
		);

		if ( OperatingSystem.IsLinux() ) {
			return ObserveLinuxProcessors( processAvailable );
		}
		if ( OperatingSystem.IsWindows() ) {
			return ObserveWindowsProcessors( processAvailable );
		}
		if ( OperatingSystem.IsMacOS() ) {
			return ObserveMacOsProcessors( processAvailable );
		}
		return ObservePortableProcessors( processAvailable );
	}

	private static ProcessorResourceSnapshot ObserveLinuxProcessors(
		HostResourceValue<int> processAvailable
	) {
		var configured = TryReadLinuxSysconfCount(
			LinuxConfiguredProcessors,
			"configured processor count"
		);
		var installed = TryReadLinuxProcessorListCount(
			"/sys/devices/system/cpu/present",
			"installed processor count"
		);
		if ( !installed.IsAvailable && configured.IsAvailable ) {
			installed = HostResourceValue<int>.Available(
				configured.GetRequiredValue(),
				configured.Provenance,
				"The configured processor count was used because sysfs did not expose the present set."
			);
		}
		var online = TryReadLinuxProcessorListCount(
			"/sys/devices/system/cpu/online",
			"online processor count"
		);
		if ( !online.IsAvailable ) {
			online = TryReadLinuxSysconfCount(
				LinuxOnlineProcessors,
				"online processor count"
			);
		}
		var affinity = TryReadLinuxAffinity();
		var quota = TryReadLinuxControlGroupQuota();
		var topology = TryReadLinuxTopology( installed, online );
		return new ProcessorResourceSnapshot(
			configured,
			installed,
			online,
			processAvailable,
			affinity,
			quota,
			topology
		);
	}

	private static ProcessorResourceSnapshot ObserveWindowsProcessors(
		HostResourceValue<int> processAvailable
	) {
		HostResourceValue<int> configured;
		HostResourceValue<int> installed;
		HostResourceValue<int> online;
		try {
			var maximum = NativeMethods.GetMaximumProcessorCount( AllProcessorGroups );
			var active = NativeMethods.GetActiveProcessorCount( AllProcessorGroups );
			configured = maximum > 0
				? HostResourceValue<int>.Available(
					checked((int)maximum),
					HostResourceProvenance.WindowsProcessorGroup
				)
				: HostResourceValue<int>.Unavailable(
					"GetMaximumProcessorCount returned zero.",
					HostResourceProvenance.WindowsProcessorGroup
				);
			installed = active > 0
				? HostResourceValue<int>.Available(
					checked((int)active),
					HostResourceProvenance.WindowsProcessorGroup
				)
				: HostResourceValue<int>.Unavailable(
					"GetActiveProcessorCount returned zero.",
					HostResourceProvenance.WindowsProcessorGroup
				);
			online = installed;
		} catch ( Exception ex ) when (
			ex is DllNotFoundException
			or EntryPointNotFoundException
			or PlatformNotSupportedException
		) {
			configured = HostResourceValue<int>.Unavailable( ex.Message, HostResourceProvenance.WindowsProcessorGroup );
			installed = configured;
			online = configured;
		}
		var affinity = TryReadWindowsAffinity();
		var quota = TryReadWindowsJobQuota( online );
		var topology = TryReadWindowsTopology( online );
		return new ProcessorResourceSnapshot(
			configured,
			installed,
			online,
			processAvailable,
			affinity,
			quota,
			topology
		);
	}

	private static ProcessorResourceSnapshot ObserveMacOsProcessors(
		HostResourceValue<int> processAvailable
	) {
		var configured = TryReadMacOsSysctlInt( "hw.logicalcpu_max" );
		if ( !configured.IsAvailable ) {
			configured = TryReadMacOsSysctlInt( "hw.ncpu" );
		}
		var installed = configured;
		var online = TryReadMacOsSysctlInt( "hw.logicalcpu" );
		if ( !online.IsAvailable ) {
			online = TryReadMacOsSysctlInt( "hw.ncpu" );
		}
		var affinity = HostResourceValue<ProcessorAffinityDescriptor>.Unsupported(
			"macOS does not expose a stable process-affinity mask through the supported provider boundary.",
			HostResourceProvenance.NativeOperatingSystem
		);
		var quota = HostResourceValue<ProcessorQuotaDescriptor>.Unsupported(
			"The macOS provider does not expose a container or job-object CPU quota.",
			HostResourceProvenance.NativeOperatingSystem
		);
		var topology = TryReadMacOsTopology( online );
		return new ProcessorResourceSnapshot(
			configured,
			installed,
			online,
			processAvailable,
			affinity,
			quota,
			topology
		);
	}

	private static ProcessorResourceSnapshot ObservePortableProcessors(
		HostResourceValue<int> processAvailable
	) {
		var hostCount = HostResourceValue<int>.Unsupported(
			"This platform adapter exposes only the process-available processor count.",
			HostResourceProvenance.ManagedRuntime
		);
		var affinity = HostResourceValue<ProcessorAffinityDescriptor>.Unsupported(
			"Process affinity is not implemented for this platform."
		);
		var quota = HostResourceValue<ProcessorQuotaDescriptor>.Unsupported(
			"Processor quota inspection is not implemented for this platform."
		);
		var topology = HostResourceValue<ProcessorTopologyDescriptor>.Available(
			new ProcessorTopologyDescriptor(
				HostResourceValue<int>.Unsupported( "Processor packages are unavailable." ),
				HostResourceValue<int>.Unsupported( "Physical cores are unavailable." ),
				processAvailable,
				HostResourceValue<int>.Unsupported( "NUMA topology is unavailable." )
			),
			HostResourceProvenance.ManagedRuntime,
			"Only the process-available logical processor count is available."
		);
		return new ProcessorResourceSnapshot(
			hostCount,
			hostCount,
			hostCount,
			processAvailable,
			affinity,
			quota,
			topology
		);
	}

	private static HostResourceValue<int> TryReadLinuxSysconfCount(
		int name,
		string description
	) {
		try {
			var value = NativeMethods.Sysconf( name ).ToInt64();
			if ( value <= 0 || value > int.MaxValue ) {
				return HostResourceValue<int>.Unavailable(
					string.Concat( "sysconf did not return a valid ", description, "." ),
					HostResourceProvenance.NativeOperatingSystem
				);
			}
			return HostResourceValue<int>.Available(
				checked((int)value),
				HostResourceProvenance.NativeOperatingSystem
			);
		} catch ( Exception ex ) when (
			ex is DllNotFoundException
			or EntryPointNotFoundException
			or PlatformNotSupportedException
		) {
			return HostResourceValue<int>.Unavailable(
				ex.Message,
				HostResourceProvenance.NativeOperatingSystem
			);
		}
	}

	private static HostResourceValue<int> TryReadLinuxProcessorListCount(
		string path,
		string description
	) {
		try {
			if ( !File.Exists( path ) ) {
				return HostResourceValue<int>.Unavailable(
					string.Concat( "sysfs does not expose the ", description, "." ),
					HostResourceProvenance.LinuxSysFileSystem
				);
			}
			var processors = HostResourceParsers.ParseProcessorList( File.ReadAllText( path ) );
			return HostResourceValue<int>.Available(
				processors.Count,
				HostResourceProvenance.LinuxSysFileSystem
			);
		} catch ( Exception ex ) {
			return HostResourceValue<int>.Unavailable(
				ex.Message,
				HostResourceProvenance.LinuxSysFileSystem
			);
		}
	}

	private static HostResourceValue<ProcessorAffinityDescriptor> TryReadLinuxAffinity() {
		try {
			const int invalidArgument = 22;
			for ( var byteCount = 128; byteCount <= 131072; byteCount *= 2 ) {
				var mask = new byte[byteCount];
				if ( 0 == NativeMethods.SchedGetAffinity( 0, checked((nuint)mask.Length), mask ) ) {
					var ids = HostResourceParsers.GetSetBitIndices( mask );
					if ( ids.Count == 0 ) {
						return HostResourceValue<ProcessorAffinityDescriptor>.Unavailable(
							"sched_getaffinity returned an empty mask.",
							HostResourceProvenance.NativeOperatingSystem
						);
					}
					return HostResourceValue<ProcessorAffinityDescriptor>.Available(
						new ProcessorAffinityDescriptor( ids.Select( static value => (long)value ), isComplete: true ),
						HostResourceProvenance.NativeOperatingSystem,
						"The effective kernel mask includes scheduler affinity and cgroup cpuset restrictions."
					);
				}

				var error = Marshal.GetLastPInvokeError();
				if ( error != invalidArgument ) {
					return HostResourceValue<ProcessorAffinityDescriptor>.Unavailable(
						string.Concat(
							"sched_getaffinity failed with native error ",
							error.ToString( CultureInfo.InvariantCulture ),
							"."
						),
						HostResourceProvenance.NativeOperatingSystem
					);
				}
			}
			return HostResourceValue<ProcessorAffinityDescriptor>.Unavailable(
				"The scheduler affinity mask exceeded the provider's 131,072-byte safety limit.",
				HostResourceProvenance.NativeOperatingSystem
			);
		} catch ( Exception ex ) when (
			ex is DllNotFoundException
			or EntryPointNotFoundException
			or PlatformNotSupportedException
		) {
			return HostResourceValue<ProcessorAffinityDescriptor>.Unsupported(
				ex.Message,
				HostResourceProvenance.NativeOperatingSystem
			);
		}
	}

	private static HostResourceValue<ProcessorAffinityDescriptor> TryReadWindowsAffinity() {
		try {
			var process = NativeMethods.GetCurrentProcess();
			try {
				_ = NativeMethods.GetProcessDefaultCpuSets(
					process,
					null,
					0,
					out var requiredCount
				);
				if ( requiredCount > 0 ) {
					var cpuSetIds = new uint[checked((int)requiredCount)];
					if (
						NativeMethods.GetProcessDefaultCpuSets(
							process,
							cpuSetIds,
							checked((uint)cpuSetIds.Length),
							out requiredCount
						)
						&& requiredCount > 0
					) {
						return HostResourceValue<ProcessorAffinityDescriptor>.Available(
							new ProcessorAffinityDescriptor(
								cpuSetIds
									.Take( checked((int)Math.Min( requiredCount, checked((uint)cpuSetIds.Length) )) )
									.Select( static value => (long)value ),
								isComplete: true,
								identifierKind: ProcessorSelectionIdentifierKind.WindowsCpuSetId
							),
							HostResourceProvenance.WindowsProcessorSet
						);
					}
				}
			} catch ( EntryPointNotFoundException ) {
				// Older Windows hosts fall back to the process affinity mask.
			}

			if ( !NativeMethods.GetProcessAffinityMask( process, out var processMask, out _ ) ) {
				return HostResourceValue<ProcessorAffinityDescriptor>.Unavailable(
					string.Concat(
						"GetProcessAffinityMask failed with native error ",
						Marshal.GetLastPInvokeError().ToString( CultureInfo.InvariantCulture ),
						"."
					),
					HostResourceProvenance.WindowsProcessorGroup
				);
			}
			var rawMask = processMask.ToUInt64();
			var bytes = BitConverter.GetBytes( rawMask );
			var ids = HostResourceParsers.GetSetBitIndices( bytes );
			if ( ids.Count == 0 ) {
				return HostResourceValue<ProcessorAffinityDescriptor>.Unavailable(
					"GetProcessAffinityMask returned an empty process mask.",
					HostResourceProvenance.WindowsProcessorGroup
				);
			}
			var groupCount = NativeMethods.GetActiveProcessorGroupCount();
			return HostResourceValue<ProcessorAffinityDescriptor>.Available(
				new ProcessorAffinityDescriptor(
					ids.Select( static value => (long)value ),
					isComplete: groupCount <= 1
				),
				HostResourceProvenance.WindowsProcessorGroup,
				groupCount <= 1
					? null
					: "The legacy affinity mask describes only the current processor group."
			);
		} catch ( Exception ex ) when (
			ex is DllNotFoundException
			or EntryPointNotFoundException
			or PlatformNotSupportedException
		) {
			return HostResourceValue<ProcessorAffinityDescriptor>.Unsupported(
				ex.Message,
				HostResourceProvenance.WindowsProcessorGroup
			);
		}
	}

	private static HostResourceValue<ProcessorQuotaDescriptor> TryReadLinuxControlGroupQuota() {
		try {
			if ( !TryReadNonEmptyText( "/proc/self/cgroup", out var membership ) ) {
				return HostResourceValue<ProcessorQuotaDescriptor>.Unavailable(
					"The process cgroup membership file is unavailable.",
					HostResourceProvenance.LinuxProcFileSystem
				);
			}
			foreach ( var line in membership.Split( '\n', StringSplitOptions.RemoveEmptyEntries ) ) {
				var fields = line.Trim().Split( ':', 3 );
				if ( fields.Length != 3 ) {
					continue;
				}
				if ( fields[0] == "0" && fields[1].Length == 0 ) {
					var path = BuildControlGroupPath( "/sys/fs/cgroup", fields[2], "cpu.max" );
					if ( TryReadNonEmptyText( path, out var cpuMax ) ) {
						return HostResourceParsers.ParseControlGroupV2CpuMax( cpuMax );
					}
				}
				var controllers = fields[1].Split( ',' );
				if ( controllers.Contains( "cpu", StringComparer.Ordinal ) ) {
					foreach ( var root in new[] { "/sys/fs/cgroup/cpu", "/sys/fs/cgroup/cpu,cpuacct" } ) {
						var quotaPath = BuildControlGroupPath( root, fields[2], "cpu.cfs_quota_us" );
						var periodPath = BuildControlGroupPath( root, fields[2], "cpu.cfs_period_us" );
						if (
							TryReadNonEmptyText( quotaPath, out var quota )
							&& TryReadNonEmptyText( periodPath, out var period )
						) {
							return HostResourceParsers.ParseControlGroupV1CpuQuota( quota, period );
						}
					}
				}
			}
			return HostResourceValue<ProcessorQuotaDescriptor>.NotApplicable(
				"No cgroup CPU hard quota applies to the current process.",
				HostResourceProvenance.LinuxProcFileSystem
			);
		} catch ( Exception ex ) {
			return HostResourceValue<ProcessorQuotaDescriptor>.Unavailable(
				ex.Message,
				HostResourceProvenance.LinuxProcFileSystem
			);
		}
	}

	private static HostResourceValue<ProcessorQuotaDescriptor> TryReadWindowsJobQuota(
		HostResourceValue<int> online
	) {
		try {
			var process = NativeMethods.GetCurrentProcess();
			if ( !NativeMethods.IsProcessInJob( process, IntPtr.Zero, out var inJob ) ) {
				return HostResourceValue<ProcessorQuotaDescriptor>.Unavailable(
					string.Concat(
						"IsProcessInJob failed with native error ",
						Marshal.GetLastPInvokeError().ToString( CultureInfo.InvariantCulture ),
						"."
					),
					HostResourceProvenance.WindowsJobObject
				);
			}
			if ( !inJob ) {
				return HostResourceValue<ProcessorQuotaDescriptor>.NotApplicable(
					"The current process is not assigned to a Windows job object.",
					HostResourceProvenance.WindowsJobObject
				);
			}
			if (
				!NativeMethods.QueryInformationJobObject(
					IntPtr.Zero,
					JobObjectCpuRateControlInformation,
					out var information,
					checked((uint)Marshal.SizeOf<NativeMethods.JobObjectCpuRateControlInformation>()),
					IntPtr.Zero
				)
			) {
				return HostResourceValue<ProcessorQuotaDescriptor>.Unavailable(
					string.Concat(
						"QueryInformationJobObject failed with native error ",
						Marshal.GetLastPInvokeError().ToString( CultureInfo.InvariantCulture ),
						"."
					),
					HostResourceProvenance.WindowsJobObject
				);
			}
			if ( 0 == (information.ControlFlags & JobObjectCpuRateControlEnable) ) {
				return HostResourceValue<ProcessorQuotaDescriptor>.NotApplicable(
					"The current job object does not enable CPU rate control.",
					HostResourceProvenance.WindowsJobObject
				);
			}
			if ( 0 != (information.ControlFlags & JobObjectCpuRateControlWeightBased) ) {
				return HostResourceValue<ProcessorQuotaDescriptor>.Unavailable(
					"The current job object uses relative CPU weights rather than a hard processor quota.",
					HostResourceProvenance.WindowsJobObject
				);
			}
			var onlineCount = online.IsAvailable
				? online.GetRequiredValue()
				: Math.Max( 1, Environment.ProcessorCount );
			double rate;
			string scope;
			if ( 0 != (information.ControlFlags & JobObjectCpuRateControlHardCap) ) {
				rate = information.CpuRate;
				scope = "Windows job object hard cap";
			} else if ( 0 != (information.ControlFlags & JobObjectCpuRateControlMinMaxRate) ) {
				rate = information.CpuRate >> 16;
				scope = "Windows job object maximum rate";
			} else {
				return HostResourceValue<ProcessorQuotaDescriptor>.NotApplicable(
					"The current job object does not impose a hard or maximum CPU-rate cap.",
					HostResourceProvenance.WindowsJobObject
				);
			}
			if ( rate <= 0 ) {
				return HostResourceValue<ProcessorQuotaDescriptor>.Unavailable(
					"The current job object reported an invalid zero CPU-rate cap.",
					HostResourceProvenance.WindowsJobObject
				);
			}
			var processorLimit = onlineCount * (rate / 10000d);
			return HostResourceValue<ProcessorQuotaDescriptor>.Available(
				new ProcessorQuotaDescriptor(
					processorLimit,
					null,
					null,
					scope
				),
				HostResourceProvenance.WindowsJobObject,
				"The job CPU rate was converted from ten-thousandths of total active processor capacity."
			);
		} catch ( Exception ex ) when (
			ex is DllNotFoundException
			or EntryPointNotFoundException
			or PlatformNotSupportedException
		) {
			return HostResourceValue<ProcessorQuotaDescriptor>.Unsupported(
				ex.Message,
				HostResourceProvenance.WindowsJobObject
			);
		}
	}

	private static HostResourceValue<ProcessorTopologyDescriptor> TryReadLinuxTopology(
		HostResourceValue<int> installed,
		HostResourceValue<int> online
	) {
		try {
			var packages = new HashSet<int>();
			var cores = new HashSet<(int Package, int Core)>();
			var logicalCount = 0;
			const string cpuRoot = "/sys/devices/system/cpu";
			if ( Directory.Exists( cpuRoot ) ) {
				foreach ( var directory in Directory.EnumerateDirectories( cpuRoot, "cpu*" ) ) {
					var name = System.IO.Path.GetFileName( directory );
					if (
						name.Length <= 3
						|| !int.TryParse(
							name[3..],
							NumberStyles.None,
							CultureInfo.InvariantCulture,
							out _
						)
					) {
						continue;
					}
					logicalCount++;
					if (
						TryReadInt32(
							System.IO.Path.Combine( directory, "topology", "physical_package_id" ),
							out var package
						)
						&& TryReadInt32( System.IO.Path.Combine( directory, "topology", "core_id" ), out var core )
					) {
						packages.Add( package );
						cores.Add( (package, core) );
					}
				}
			}
			var numaCount = CountNumberedDirectories( "/sys/devices/system/node", "node" );
			var logical = logicalCount > 0
				? HostResourceValue<int>.Available( logicalCount, HostResourceProvenance.LinuxSysFileSystem )
				: installed.IsAvailable ? installed : online;
			var descriptor = new ProcessorTopologyDescriptor(
				packages.Count > 0
					? HostResourceValue<int>.Available( packages.Count, HostResourceProvenance.LinuxSysFileSystem )
					: HostResourceValue<int>.Unavailable(
						"sysfs did not expose processor package identifiers.",
						HostResourceProvenance.LinuxSysFileSystem
					),
				cores.Count > 0
					? HostResourceValue<int>.Available( cores.Count, HostResourceProvenance.LinuxSysFileSystem )
					: HostResourceValue<int>.Unavailable(
						"sysfs did not expose physical core identifiers.",
						HostResourceProvenance.LinuxSysFileSystem
					),
				logical,
				numaCount > 0
					? HostResourceValue<int>.Available( numaCount, HostResourceProvenance.LinuxSysFileSystem )
					: HostResourceValue<int>.Unavailable(
						"sysfs did not expose NUMA node directories.",
						HostResourceProvenance.LinuxSysFileSystem
					)
			);
			return HostResourceValue<ProcessorTopologyDescriptor>.Available(
				descriptor,
				HostResourceProvenance.LinuxSysFileSystem
			);
		} catch ( Exception ex ) {
			return HostResourceValue<ProcessorTopologyDescriptor>.Unavailable(
				ex.Message,
				HostResourceProvenance.LinuxSysFileSystem
			);
		}
	}

	private static HostResourceValue<ProcessorTopologyDescriptor> TryReadWindowsTopology(
		HostResourceValue<int> online
	) {
		try {
			uint length = 0;
			_ = NativeMethods.GetLogicalProcessorInformationEx(
				NativeMethods.LogicalProcessorRelationship.All,
				IntPtr.Zero,
				ref length
			);
			if ( length == 0 ) {
				return HostResourceValue<ProcessorTopologyDescriptor>.Unavailable(
					"GetLogicalProcessorInformationEx did not report a buffer size.",
					HostResourceProvenance.NativeOperatingSystem
				);
			}
			var buffer = Marshal.AllocHGlobal( checked((int)length) );
			try {
				if (
					!NativeMethods.GetLogicalProcessorInformationEx(
						NativeMethods.LogicalProcessorRelationship.All,
						buffer,
						ref length
					)
				) {
					return HostResourceValue<ProcessorTopologyDescriptor>.Unavailable(
						string.Concat(
							"GetLogicalProcessorInformationEx failed with native error ",
							Marshal.GetLastPInvokeError().ToString( CultureInfo.InvariantCulture ),
							"."
						),
						HostResourceProvenance.NativeOperatingSystem
					);
				}
				var packages = 0;
				var cores = 0;
				var numaNodes = 0;
				uint offset = 0;
				while ( offset < length ) {
					var relationship = (NativeMethods.LogicalProcessorRelationship)Marshal.ReadInt32(
						buffer,
						checked((int)offset)
					);
					var itemLength = Marshal.ReadInt32( buffer, checked((int)offset + sizeof( int )) );
					if ( itemLength < 8 || offset + checked((uint)itemLength) > length ) {
						return HostResourceValue<ProcessorTopologyDescriptor>.Unavailable(
							"GetLogicalProcessorInformationEx returned a malformed record.",
							HostResourceProvenance.NativeOperatingSystem
						);
					}
					switch ( relationship ) {
						case NativeMethods.LogicalProcessorRelationship.ProcessorCore:
							cores++;
							break;
						case NativeMethods.LogicalProcessorRelationship.NumaNode:
						case NativeMethods.LogicalProcessorRelationship.NumaNodeEx:
							numaNodes++;
							break;
						case NativeMethods.LogicalProcessorRelationship.ProcessorPackage:
							packages++;
							break;
					}
					offset += checked((uint)itemLength);
				}
				var descriptor = new ProcessorTopologyDescriptor(
					packages > 0
						? HostResourceValue<int>.Available( packages, HostResourceProvenance.NativeOperatingSystem )
						: HostResourceValue<int>.Unavailable(
							"Processor packages were not reported.",
							HostResourceProvenance.NativeOperatingSystem
						),
					cores > 0
						? HostResourceValue<int>.Available( cores, HostResourceProvenance.NativeOperatingSystem )
						: HostResourceValue<int>.Unavailable(
							"Physical cores were not reported.",
							HostResourceProvenance.NativeOperatingSystem
						),
					online,
					numaNodes > 0
						? HostResourceValue<int>.Available( numaNodes, HostResourceProvenance.NativeOperatingSystem )
						: HostResourceValue<int>.Unavailable(
							"NUMA nodes were not reported.",
							HostResourceProvenance.NativeOperatingSystem
						)
				);
				return HostResourceValue<ProcessorTopologyDescriptor>.Available(
					descriptor,
					HostResourceProvenance.NativeOperatingSystem
				);
			} finally {
				Marshal.FreeHGlobal( buffer );
			}
		} catch ( Exception ex ) when (
			ex is DllNotFoundException
			or EntryPointNotFoundException
			or PlatformNotSupportedException
		) {
			return HostResourceValue<ProcessorTopologyDescriptor>.Unsupported(
				ex.Message,
				HostResourceProvenance.NativeOperatingSystem
			);
		}
	}

	private static HostResourceValue<ProcessorTopologyDescriptor> TryReadMacOsTopology(
		HostResourceValue<int> online
	) {
		var packages = TryReadMacOsSysctlInt( "hw.packages" );
		var cores = TryReadMacOsSysctlInt( "hw.physicalcpu" );
		var logical = TryReadMacOsSysctlInt( "hw.logicalcpu" );
		if ( !logical.IsAvailable ) {
			logical = online;
		}
		var descriptor = new ProcessorTopologyDescriptor(
			packages,
			cores,
			logical,
			HostResourceValue<int>.Unsupported(
				"macOS does not expose a stable NUMA-node inventory through the supported provider boundary.",
				HostResourceProvenance.MacOsSysctl
			)
		);
		return HostResourceValue<ProcessorTopologyDescriptor>.Available(
			descriptor,
			HostResourceProvenance.MacOsSysctl
		);
	}

	private static HostResourceValue<int> TryReadMacOsSysctlInt( string name ) {
		try {
			var value = 0;
			nuint length = checked((nuint)sizeof( int ));
			if ( 0 != NativeMethods.SysctlByName( name, ref value, ref length, IntPtr.Zero, 0 ) || value <= 0 ) {
				return HostResourceValue<int>.Unavailable(
					string.Concat( "The macOS sysctl ", name, " is unavailable." ),
					HostResourceProvenance.MacOsSysctl
				);
			}
			return HostResourceValue<int>.Available(
				value,
				HostResourceProvenance.MacOsSysctl
			);
		} catch ( Exception ex ) when (
			ex is DllNotFoundException
			or EntryPointNotFoundException
			or PlatformNotSupportedException
		) {
			return HostResourceValue<int>.Unavailable(
				ex.Message,
				HostResourceProvenance.MacOsSysctl
			);
		}
	}

	private static string BuildControlGroupPath(
		string root,
		string membershipPath,
		string fileName
	) {
		var rootPath = System.IO.Path.GetFullPath( root );
		var relative = membershipPath.Trim().TrimStart( '/', '\\' );
		var candidate = System.IO.Path.GetFullPath( System.IO.Path.Combine( rootPath, relative, fileName ) );
		var rootPrefix = string.Concat(
			rootPath.TrimEnd( System.IO.Path.DirectorySeparatorChar ),
			System.IO.Path.DirectorySeparatorChar
		);
		if ( !candidate.StartsWith( rootPrefix, StringComparison.Ordinal ) ) {
			throw new InvalidDataException( "The cgroup membership path escapes its controller root." );
		}
		return candidate;
	}

	private static int CountNumberedDirectories(
		string root,
		string prefix
	) {
		if ( !Directory.Exists( root ) ) {
			return 0;
		}
		var count = 0;
		foreach ( var directory in Directory.EnumerateDirectories( root, string.Concat( prefix, "*" ) ) ) {
			var name = System.IO.Path.GetFileName( directory );
			if (
				name.Length > prefix.Length
				&& int.TryParse( name[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out _ )
			) {
				count++;
			}
		}
		return count;
	}

	private static bool TryReadInt32(
		string path,
		out int value
	) {
		value = 0;
		return TryReadNonEmptyText( path, out var text )
			&& int.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value );
	}

	private static bool TryReadNonEmptyText(
		string path,
		out string value
	) {
		value = string.Empty;
		try {
			if ( !File.Exists( path ) ) {
				return false;
			}
			value = File.ReadAllText( path ).Trim();
			return value.Length > 0;
		} catch {
			return false;
		}
	}

#pragma warning disable CS0649 // Native output structures are populated by P/Invoke.
// Keep native host-resource adapters self-contained and compatible with the existing project settings.
#pragma warning disable SYSLIB1054
	private static class NativeMethods {
		/// <summary>Gets the native Unix host identifier.</summary>
		/// <returns>The native host identifier.</returns>
		[DllImport( "libc", EntryPoint = "gethostid", SetLastError = false )]
		internal static extern nint GetHostId();

		/// <summary>Reads one native Unix system-configuration value.</summary>
		/// <param name="name">The platform configuration selector.</param>
		/// <returns>The configuration value, or a negative result on failure.</returns>
		[DllImport( "libc", EntryPoint = "sysconf", SetLastError = true )]
		internal static extern nint Sysconf( int name );

		/// <summary>Reads the Linux scheduler affinity mask for a process.</summary>
		/// <param name="processId">The process identifier, or zero for the current process.</param>
		/// <param name="cpuSetSize">The mask-buffer size.</param>
		/// <param name="mask">The destination mask.</param>
		/// <returns>Zero on success; otherwise, a native error result.</returns>
		[DllImport( "libc", EntryPoint = "sched_getaffinity", SetLastError = true )]
		internal static extern int SchedGetAffinity(
			int processId,
			nuint cpuSetSize,
			[Out] byte[] mask
		);

		/// <summary>Reads one integer-valued macOS sysctl.</summary>
		/// <param name="name">The sysctl name.</param>
		/// <param name="oldValue">The destination value.</param>
		/// <param name="oldLength">The destination size.</param>
		/// <param name="newValue">The unused replacement pointer.</param>
		/// <param name="newLength">The unused replacement size.</param>
		/// <returns>Zero on success; otherwise, a native error result.</returns>
		[DllImport( "libSystem.B.dylib", EntryPoint = "sysctlbyname", SetLastError = true )]
		internal static extern int SysctlByName(
			[MarshalAs( UnmanagedType.LPUTF8Str )] string name,
			ref int oldValue,
			ref nuint oldLength,
			IntPtr newValue,
			nuint newLength
		);

		/// <summary>Gets the pseudo-handle for the current Windows process.</summary>
		/// <returns>The process pseudo-handle.</returns>
		[DllImport( "kernel32.dll", EntryPoint = "GetCurrentProcess", ExactSpelling = true )]
		internal static extern IntPtr GetCurrentProcess();

		/// <summary>Gets the maximum processor count for a Windows processor group or all groups.</summary>
		/// <param name="groupNumber">The group number or the all-groups sentinel.</param>
		/// <returns>The maximum processor count.</returns>
		[DllImport( "kernel32.dll", EntryPoint = "GetMaximumProcessorCount", ExactSpelling = true )]
		internal static extern uint GetMaximumProcessorCount( ushort groupNumber );

		/// <summary>Gets the active processor count for a Windows processor group or all groups.</summary>
		/// <param name="groupNumber">The group number or the all-groups sentinel.</param>
		/// <returns>The active processor count.</returns>
		[DllImport( "kernel32.dll", EntryPoint = "GetActiveProcessorCount", ExactSpelling = true )]
		internal static extern uint GetActiveProcessorCount( ushort groupNumber );

		/// <summary>Gets the number of active Windows processor groups.</summary>
		/// <returns>The active processor-group count.</returns>
		[DllImport( "kernel32.dll", EntryPoint = "GetActiveProcessorGroupCount", ExactSpelling = true )]
		internal static extern ushort GetActiveProcessorGroupCount();

		/// <summary>Reads the current process and system affinity masks.</summary>
		/// <param name="process">The process handle.</param>
		/// <param name="processAffinityMask">The process mask.</param>
		/// <param name="systemAffinityMask">The system mask for the current group.</param>
		/// <returns><see langword="true"/> on success.</returns>
		[DllImport( "kernel32.dll", EntryPoint = "GetProcessAffinityMask", ExactSpelling = true, SetLastError = true )]
		[return: MarshalAs( UnmanagedType.Bool )]
		internal static extern bool GetProcessAffinityMask(
			IntPtr process,
			out UIntPtr processAffinityMask,
			out UIntPtr systemAffinityMask
		);

		/// <summary>Reads the current process default Windows CPU-set identifiers.</summary>
		/// <param name="process">The process handle.</param>
		/// <param name="cpuSetIds">The destination CPU-set identifiers.</param>
		/// <param name="cpuSetIdCount">The destination capacity.</param>
		/// <param name="requiredIdCount">The required or returned identifier count.</param>
		/// <returns><see langword="true"/> on success.</returns>
		[DllImport(
			"kernel32.dll",
			EntryPoint = "GetProcessDefaultCpuSets",
			ExactSpelling = true,
			SetLastError = true
		)]
		[return: MarshalAs( UnmanagedType.Bool )]
		internal static extern bool GetProcessDefaultCpuSets(
			IntPtr process,
			[Out] uint[]? cpuSetIds,
			uint cpuSetIdCount,
			out uint requiredIdCount
		);

		/// <summary>Determines whether a Windows process belongs to a job object.</summary>
		/// <param name="process">The process handle.</param>
		/// <param name="job">The job handle or a null handle for any job.</param>
		/// <param name="result">Whether the process belongs to the requested job.</param>
		/// <returns><see langword="true"/> on success.</returns>
		[DllImport( "kernel32.dll", EntryPoint = "IsProcessInJob", ExactSpelling = true, SetLastError = true )]
		[return: MarshalAs( UnmanagedType.Bool )]
		internal static extern bool IsProcessInJob(
			IntPtr process,
			IntPtr job,
			[MarshalAs( UnmanagedType.Bool )] out bool result
		);

		/// <summary>Reads Windows job-object CPU-rate information.</summary>
		/// <param name="job">The job handle or a null handle for the current job.</param>
		/// <param name="informationClass">The job information class.</param>
		/// <param name="information">The returned CPU-rate information.</param>
		/// <param name="informationLength">The destination structure size.</param>
		/// <param name="returnLength">The optional returned-size pointer.</param>
		/// <returns><see langword="true"/> on success.</returns>
		[DllImport(
			"kernel32.dll",
			EntryPoint = "QueryInformationJobObject",
			ExactSpelling = true,
			SetLastError = true
		)]
		[return: MarshalAs( UnmanagedType.Bool )]
		internal static extern bool QueryInformationJobObject(
			IntPtr job,
			int informationClass,
			out JobObjectCpuRateControlInformation information,
			uint informationLength,
			IntPtr returnLength
		);

		/// <summary>Reads group-aware Windows logical-processor topology records.</summary>
		/// <param name="relationship">The requested relationship class.</param>
		/// <param name="buffer">The destination buffer or a null pointer for sizing.</param>
		/// <param name="returnedLength">The required or returned byte count.</param>
		/// <returns><see langword="true"/> on success.</returns>
		[DllImport(
			"kernel32.dll",
			EntryPoint = "GetLogicalProcessorInformationEx",
			ExactSpelling = true,
			SetLastError = true
		)]
		[return: MarshalAs( UnmanagedType.Bool )]
		internal static extern bool GetLogicalProcessorInformationEx(
			LogicalProcessorRelationship relationship,
			IntPtr buffer,
			ref uint returnedLength
		);

		/// <summary>Matches the Windows job-object CPU-rate control structure.</summary>
		[StructLayout( LayoutKind.Sequential )]
		internal struct JobObjectCpuRateControlInformation {
			/// <summary>Gets the native CPU-rate control flags.</summary>
			internal uint ControlFlags;
			/// <summary>Gets the native rate, weight, or packed minimum/maximum values.</summary>
			internal uint CpuRate;
		}

		/// <summary>Identifies Windows logical-processor topology relationships.</summary>
		internal enum LogicalProcessorRelationship {
			/// <summary>A physical processor core.</summary>
			ProcessorCore = 0,
			/// <summary>A legacy NUMA-node relationship.</summary>
			NumaNode = 1,
			/// <summary>A processor cache.</summary>
			Cache = 2,
			/// <summary>A physical processor package.</summary>
			ProcessorPackage = 3,
			/// <summary>A processor group.</summary>
			Group = 4,
			/// <summary>A processor die.</summary>
			ProcessorDie = 5,
			/// <summary>An extended NUMA-node relationship.</summary>
			NumaNodeEx = 6,
			/// <summary>A processor module.</summary>
			ProcessorModule = 7,
			/// <summary>All supported relationship types.</summary>
			All = 0xffff
		}
	}
#pragma warning restore SYSLIB1054
#pragma warning restore CS0649
}
