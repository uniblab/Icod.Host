namespace Icod.Host.Sample;

using Icod.Host;

/// <summary>Demonstrates the standalone host-resource provider.</summary>
public static class Program {
	/// <summary>Observes and prints selected host and processor facts.</summary>
	public static async Task Main() {
		HostResourceSnapshot snapshot = await SystemHostResourceProvider.Instance.ObserveAsync();

		Console.WriteLine(
			snapshot.HostIdentifier.IsAvailable
				? $"Host ID: {snapshot.HostIdentifier.GetRequiredValue().Hexadecimal}"
				: $"Host ID: {snapshot.HostIdentifier.Availability}"
		);

		ProcessorResourceSnapshot processors = snapshot.Processors;
		Console.WriteLine(
			$"Process-available processors: {processors.ProcessAvailableProcessorCount.GetRequiredValue()}"
		);
		WriteObservation(
			"Configured processors",
			processors.ConfiguredProcessorCount
		);
		WriteObservation(
			"Online processors",
			processors.OnlineProcessorCount
		);

		if ( processors.Affinity.IsAvailable ) {
			ProcessorAffinityDescriptor affinity = processors.Affinity.GetRequiredValue();
			Console.WriteLine(
				$"Affinity selection: {affinity.Count} processor(s); complete={affinity.IsComplete}"
			);
		} else {
			Console.WriteLine(
				$"Affinity selection: {processors.Affinity.Availability}"
			);
		}

		if ( processors.Quota.IsAvailable ) {
			Console.WriteLine(
				$"CPU quota: {processors.Quota.GetRequiredValue().ProcessorLimit:0.###} processor(s)"
			);
		} else {
			Console.WriteLine(
				$"CPU quota: {processors.Quota.Availability}"
			);
		}
	}

	private static void WriteObservation(
		string label,
		HostResourceValue<int> observation
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( label );
		Console.WriteLine(
			observation.IsAvailable
				? $"{label}: {observation.GetRequiredValue()} ({observation.Provenance})"
				: $"{label}: {observation.Availability} ({observation.Provenance})"
		);
	}
}
