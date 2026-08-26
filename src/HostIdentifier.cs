namespace Icod.Host;

using System.Globalization;
using System.Text;

/// <summary>
/// Represents one normalized 32-bit host identifier and the kind of source from
/// which it was obtained.
/// </summary>
public sealed record HostIdentifier {
	/// <summary>
	/// Initializes a normalized host identifier.
	/// </summary>
	/// <param name="value">The normalized unsigned 32-bit value.</param>
	/// <param name="sourceDescription">A non-secret description of the source.</param>
	/// <exception cref="ArgumentNullException"><paramref name="sourceDescription"/> is null.</exception>
	public HostIdentifier(
		uint value,
		string sourceDescription
	) {
		Value = value;
		SourceDescription = sourceDescription ?? throw new ArgumentNullException( nameof( sourceDescription ) );
	}

	/// <summary>Gets the normalized unsigned 32-bit identifier.</summary>
	public uint Value { get; }

	/// <summary>Gets the lowercase eight-digit hexadecimal representation.</summary>
	public string Hexadecimal => HostIdentifierNormalizer.Format( Value );

	/// <summary>Gets a non-secret description of the identifier source.</summary>
	public string SourceDescription { get; }
}

/// <summary>
/// Supplies deterministic normalization for native and textual host identifiers.
/// </summary>
public static class HostIdentifierNormalizer {
	private const uint FnvOffsetBasis = 2166136261;
	private const uint FnvPrime = 16777619;

	/// <summary>
	/// Normalizes the native signed <c>gethostid</c> result to the low unsigned
	/// 32 bits used by GNU-compatible presentation.
	/// </summary>
	/// <param name="nativeValue">The native signed value.</param>
	/// <returns>The normalized unsigned value.</returns>
	public static uint NormalizeNative( long nativeValue ) {
		return unchecked((uint)nativeValue);
	}

	/// <summary>
	/// Deterministically folds a stable textual machine identifier to 32 bits.
	/// Hexadecimal identifiers are decoded before hashing; other identifiers are
	/// normalized to trimmed lowercase invariant text and encoded as UTF-8.
	/// </summary>
	/// <param name="identifier">The stable textual identifier.</param>
	/// <returns>The normalized unsigned value.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="identifier"/> is null.</exception>
	/// <exception cref="ArgumentException">The identifier is empty.</exception>
	public static uint NormalizeStableText( string identifier ) {
		ArgumentNullException.ThrowIfNull( identifier );
		var normalized = identifier.Trim();
		if ( normalized.Length == 0 ) {
			throw new ArgumentException( "A host identifier cannot be empty.", nameof( identifier ) );
		}

		var compactHex = new string(
			normalized
				.Where(
					static character => character is not '-'
						&& character is not '{'
						&& character is not '}'
						&& !char.IsWhiteSpace( character )
				)
				.ToArray()
		);
		byte[] bytes;
		if (
			compactHex.Length >= 8
			&& compactHex.Length % 2 == 0
			&& compactHex.All( static character => Uri.IsHexDigit( character ) )
		) {
			bytes = Convert.FromHexString( compactHex );
		} else {
			bytes = Encoding.UTF8.GetBytes( normalized.ToLowerInvariant() );
		}

		var hash = FnvOffsetBasis;
		foreach ( var value in bytes ) {
			hash = unchecked((hash ^ value) * FnvPrime);
		}
		return hash;
	}

	/// <summary>Formats a normalized identifier as eight lowercase hexadecimal digits.</summary>
	/// <param name="value">The normalized identifier.</param>
	/// <returns>The hexadecimal representation.</returns>
	public static string Format( uint value ) {
		return value.ToString( "x8", CultureInfo.InvariantCulture );
	}
}
