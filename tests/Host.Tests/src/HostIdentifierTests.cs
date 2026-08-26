namespace Icod.Host.Tests;

using Icod.Host;
using Xunit;

/// <summary>Tests deterministic host-identifier normalization.</summary>
public sealed class HostIdentifierTests {
	/// <summary>Verifies that signed native values retain their low 32 bits.</summary>
	[Theory]
	[InlineData( 0L, 0U )]
	[InlineData( 1L, 1U )]
	[InlineData( -1L, uint.MaxValue )]
	[InlineData( 0x123456789L, 0x23456789U )]
	public void NativeNormalizationUsesLowUnsignedBits(
		long nativeValue,
		uint expected
	) {
		Assert.Equal( expected, HostIdentifierNormalizer.NormalizeNative( nativeValue ) );
	}

	/// <summary>Verifies equivalent hexadecimal textual forms normalize identically.</summary>
	[Fact]
	public void HexadecimalTextIgnoresCommonSeparators() {
		var compact = HostIdentifierNormalizer.NormalizeStableText( "00112233445566778899aabbccddeeff" );
		var separated = HostIdentifierNormalizer.NormalizeStableText( "{00112233-4455-6677-8899-aabbccddeeff}" );

		Assert.Equal( compact, separated );
	}

	/// <summary>Verifies stable hexadecimal input has a fixed cross-platform result.</summary>
	[Fact]
	public void HexadecimalTextUsesStableFnvResult() {
		Assert.Equal(
			0xff138f15U,
			HostIdentifierNormalizer.NormalizeStableText( "00112233445566778899aabbccddeeff" )
		);
	}

	/// <summary>Verifies nonhexadecimal text is case-insensitive and trimmed.</summary>
	[Fact]
	public void TextFallbackIsTrimmedAndCaseInsensitive() {
		Assert.Equal(
			HostIdentifierNormalizer.NormalizeStableText( "example-host" ),
			HostIdentifierNormalizer.NormalizeStableText( "  EXAMPLE-HOST  " )
		);
	}

	/// <summary>Verifies GNU-style fixed-width hexadecimal formatting.</summary>
	[Fact]
	public void FormattingUsesEightLowercaseDigits() {
		Assert.Equal( "0000002a", HostIdentifierNormalizer.Format( 42 ) );
	}
}
