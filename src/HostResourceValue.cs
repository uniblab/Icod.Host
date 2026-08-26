namespace Icod.Host;

/// <summary>
/// Identifies whether one host-resource value is available and, when it is not,
/// why the provider could not supply it.
/// </summary>
public enum HostResourceAvailability {
	/// <summary>The provider could not obtain the value for this observation.</summary>
	Unavailable = 0,
	/// <summary>The value is available.</summary>
	Available = 1,
	/// <summary>The current platform does not expose the value.</summary>
	Unsupported = 2,
	/// <summary>The value does not apply to the current host or process.</summary>
	NotApplicable = 3
}

/// <summary>
/// Identifies the source from which a host-resource value was obtained.
/// </summary>
public enum HostResourceProvenance {
	/// <summary>The provider did not identify a source.</summary>
	Unknown = 0,
	/// <summary>The managed runtime supplied the value.</summary>
	ManagedRuntime = 1,
	/// <summary>A native operating-system API supplied the value.</summary>
	NativeOperatingSystem = 2,
	/// <summary>A Linux procfs file supplied the value.</summary>
	LinuxProcFileSystem = 3,
	/// <summary>A Linux sysfs file supplied the value.</summary>
	LinuxSysFileSystem = 4,
	/// <summary>A Linux cgroup v2 controller supplied the value.</summary>
	LinuxControlGroupV2 = 5,
	/// <summary>A Linux cgroup v1 controller supplied the value.</summary>
	LinuxControlGroupV1 = 6,
	/// <summary>The Windows registry supplied the value.</summary>
	WindowsRegistry = 7,
	/// <summary>A Windows processor-group API supplied the value.</summary>
	WindowsProcessorGroup = 8,
	/// <summary>A Windows CPU-set API supplied the value.</summary>
	WindowsProcessorSet = 9,
	/// <summary>A Windows job object supplied the value.</summary>
	WindowsJobObject = 10,
	/// <summary>A macOS sysctl supplied the value.</summary>
	MacOsSysctl = 11,
	/// <summary>The value was deterministically derived from other host facts.</summary>
	Derived = 12
}

/// <summary>
/// Carries one host-resource value together with explicit availability,
/// provenance, and diagnostic information.
/// </summary>
/// <typeparam name="T">The observed value type.</typeparam>
public readonly record struct HostResourceValue<T> {
	private HostResourceValue(
		HostResourceAvailability availability,
		HostResourceProvenance provenance,
		T? value,
		string? message
	) {
		Availability = availability;
		Provenance = provenance;
		Value = value;
		Message = message;
	}

	/// <summary>Gets the availability state.</summary>
	public HostResourceAvailability Availability { get; }

	/// <summary>Gets the source of the observation.</summary>
	public HostResourceProvenance Provenance { get; }

	/// <summary>Gets the observed value when it is available.</summary>
	public T? Value { get; }

	/// <summary>Gets an optional provider explanation.</summary>
	public string? Message { get; }

	/// <summary>Gets whether the value is available.</summary>
	public bool IsAvailable => Availability == HostResourceAvailability.Available;

	/// <summary>
	/// Gets the available value or throws when the value is not available.
	/// </summary>
	/// <returns>The available value.</returns>
	/// <exception cref="InvalidOperationException">The value is not available.</exception>
	public T GetRequiredValue() {
		if ( !IsAvailable ) {
			throw new InvalidOperationException( Message ?? "The host-resource value is not available." );
		}
		return Value!;
	}

	/// <summary>Creates an available observation.</summary>
	/// <param name="value">The observed value.</param>
	/// <param name="provenance">The source of the value.</param>
	/// <param name="message">An optional explanatory message.</param>
	/// <returns>The available observation.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
	public static HostResourceValue<T> Available(
		T value,
		HostResourceProvenance provenance,
		string? message = null
	) {
		ArgumentNullException.ThrowIfNull( value );
		return new HostResourceValue<T>(
			HostResourceAvailability.Available,
			provenance,
			value,
			message
		);
	}

	/// <summary>Creates an unavailable observation.</summary>
	/// <param name="message">An optional explanation.</param>
	/// <param name="provenance">The source that could not supply the value.</param>
	/// <returns>The unavailable observation.</returns>
	public static HostResourceValue<T> Unavailable(
		string? message = null,
		HostResourceProvenance provenance = HostResourceProvenance.Unknown
	) => new(
		HostResourceAvailability.Unavailable,
		provenance,
		default,
		message
	);

	/// <summary>Creates an unsupported observation.</summary>
	/// <param name="message">An optional explanation.</param>
	/// <param name="provenance">The provider boundary reporting the limitation.</param>
	/// <returns>The unsupported observation.</returns>
	public static HostResourceValue<T> Unsupported(
		string? message = null,
		HostResourceProvenance provenance = HostResourceProvenance.Unknown
	) => new(
		HostResourceAvailability.Unsupported,
		provenance,
		default,
		message
	);

	/// <summary>Creates a not-applicable observation.</summary>
	/// <param name="message">An optional explanation.</param>
	/// <param name="provenance">The provider boundary reporting the state.</param>
	/// <returns>The not-applicable observation.</returns>
	public static HostResourceValue<T> NotApplicable(
		string? message = null,
		HostResourceProvenance provenance = HostResourceProvenance.Unknown
	) => new(
		HostResourceAvailability.NotApplicable,
		provenance,
		default,
		message
	);
}
