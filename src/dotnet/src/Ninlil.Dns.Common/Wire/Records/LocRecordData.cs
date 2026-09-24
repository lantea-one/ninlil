using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.Loc" /> record,
///     defined by RFC 1876: the owner name's physical location on Earth.
/// </summary>
/// <param name="LatitudeDegrees">Latitude, in degrees; positive is north of the equator, negative south.</param>
/// <param name="LongitudeDegrees">Longitude, in degrees; positive is east of Greenwich, negative west.</param>
/// <param name="AltitudeMeters">Altitude above (or, if negative, below) the WGS 84 reference spheroid, in meters.</param>
/// <param name="DiameterMeters">The diameter of a sphere enclosing whatever the location describes, in meters.</param>
/// <param name="HorizontalPrecisionMeters">The horizontal measurement error, in meters.</param>
/// <param name="VerticalPrecisionMeters">The vertical measurement error, in meters.</param>
public record LocRecordData(
    double LatitudeDegrees,
    double LongitudeDegrees,
    double AltitudeMeters,
    double DiameterMeters,
    double HorizontalPrecisionMeters,
    double VerticalPrecisionMeters
) : IDnsRecordData;

/// <summary>
///     This class reads and writes <see cref="LocRecordData" />. RFC 1876 packs its sixteen
///     octets unusually densely, using two encodings not seen anywhere else in this namespace:
///     <list type="bullet">
///         <item>
///             <description>
///                 <see cref="LocRecordData.DiameterMeters" />, <see cref="LocRecordData.HorizontalPrecisionMeters" />, and
///                 <see cref="LocRecordData.VerticalPrecisionMeters" /> are each packed into a single octet, as
///                 a base (the high nibble, 0-9) and a power-of-ten exponent (the low nibble,
///                 0-9) that together mean <c>base * 10^exponent</c> centimeters -- "unsigned
///                 exponential notation", in the RFC's own words.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <see cref="LocRecordData.LatitudeDegrees" /> and <see cref="LocRecordData.LongitudeDegrees" /> are each a
///                 32-bit unsigned integer counting thousandths of an arc-second away from the
///                 equator (for latitude) or the prime meridian (for longitude), offset so that
///                 2^31 itself represents zero -- which is what lets a single unsigned integer
///                 represent both hemispheres without a separate sign.
///             </description>
///         </item>
///     </list>
/// </summary>
public class LocRecordDataCodec : IDnsRecordDataCodec
{
    /// <summary>
    ///     This constant contains the unsigned integer value RFC 1876 assigns to zero degrees of
    ///     latitude or longitude -- the equator, or the prime meridian.
    /// </summary>
    private const uint AngleZeroValue = 1u << 31;

    /// <summary>
    ///     This constant contains, in centimeters, how far below the WGS 84 reference spheroid
    ///     RFC 1876's altitude encoding measures from, so that a below-spheroid altitude can
    ///     still be represented by an unsigned integer.
    /// </summary>
    private const long AltitudeBaseCentimeters = 100_000 * 100;

    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Loc;

    /// <inheritdoc />
    public Type DataType => typeof(LocRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata)
    {
        // Byte 0 is the format version, always 0 for the format this codec implements.
        byte diameter = rdata[1];
        byte horizontalPrecision = rdata[2];
        byte verticalPrecision = rdata[3];
        uint latitude = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(4, 4));
        uint longitude = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(8, 4));
        uint altitude = BinaryPrimitives.ReadUInt32BigEndian(rdata.Slice(12, 4));

        return new LocRecordData(
            DecodeAngle(latitude),
            DecodeAngle(longitude),
            DecodeAltitude(altitude),
            DecodeScaled(diameter),
            DecodeScaled(horizontalPrecision),
            DecodeScaled(verticalPrecision)
        );
    }

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        LocRecordData typed = (LocRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        // Version is always 0.
        buffer.GetSpan(1)[0] = 0;
        buffer.Advance(1);

        buffer.GetSpan(1)[0] = EncodeScaled(typed.DiameterMeters);
        buffer.Advance(1);
        buffer.GetSpan(1)[0] = EncodeScaled(typed.HorizontalPrecisionMeters);
        buffer.Advance(1);
        buffer.GetSpan(1)[0] = EncodeScaled(typed.VerticalPrecisionMeters);
        buffer.Advance(1);

        WriteUInt32(buffer, EncodeAngle(typed.LatitudeDegrees));
        WriteUInt32(buffer, EncodeAngle(typed.LongitudeDegrees));
        WriteUInt32(buffer, EncodeAltitude(typed.AltitudeMeters));

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method decodes one of RFC 1876's "unsigned exponential notation" bytes into
    ///     meters.
    /// </summary>
    private static double DecodeScaled(byte value)
    {
        int scaleBase = (value >> 4) & 0x0F;
        int exponent = value & 0x0F;
        double centimeters = scaleBase * Math.Pow(10, exponent);
        return centimeters / 100.0;
    }

    /// <summary>
    ///     This method encodes a value in meters into one of RFC 1876's "unsigned exponential
    ///     notation" bytes, choosing the base/exponent pair that reproduces it most closely.
    /// </summary>
    private static byte EncodeScaled(double meters)
    {
        long centimeters = (long)Math.Round(meters * 100.0, MidpointRounding.AwayFromZero);

        if (centimeters <= 0)
            return 0;

        int exponent = Math.Clamp((int)Math.Floor(Math.Log10(centimeters)), 0, 9);
        long divisor = (long)Math.Pow(10, exponent);
        long scaleBase = (long)
            Math.Round(centimeters / (double)divisor, MidpointRounding.AwayFromZero);

        // Rounding the base up can occasionally push it to 10, which no longer fits in a nibble;
        // when that happens, shift one more order of magnitude into the exponent instead.
        if (scaleBase > 9)
        {
            scaleBase = 1;
            exponent = Math.Min(exponent + 1, 9);
        }

        return (byte)(((byte)scaleBase << 4) | (byte)exponent);
    }

    /// <summary>
    ///     This method decodes an encoded latitude or longitude into degrees.
    /// </summary>
    private static double DecodeAngle(uint value)
    {
        long milliArcSecondsFromZero = (long)value - AngleZeroValue;
        return milliArcSecondsFromZero / 1000.0 / 3600.0;
    }

    /// <summary>
    ///     This method encodes a latitude or longitude, in degrees, into RFC 1876's unsigned,
    ///     zero-offset representation.
    /// </summary>
    private static uint EncodeAngle(double degrees)
    {
        double arcSeconds = degrees * 3600.0;
        long milliArcSeconds = (long)Math.Round(arcSeconds * 1000.0, MidpointRounding.AwayFromZero);
        long value = milliArcSeconds + AngleZeroValue;
        return (uint)Math.Clamp(value, 0, uint.MaxValue);
    }

    /// <summary>
    ///     This method decodes an encoded altitude into meters.
    /// </summary>
    private static double DecodeAltitude(uint value)
    {
        long centimetersAboveBase = (long)value - AltitudeBaseCentimeters;
        return centimetersAboveBase / 100.0;
    }

    /// <summary>
    ///     This method encodes an altitude, in meters, into RFC 1876's unsigned,
    ///     below-the-spheroid-offset representation.
    /// </summary>
    private static uint EncodeAltitude(double meters)
    {
        long centimeters =
            (long)Math.Round(meters * 100.0, MidpointRounding.AwayFromZero)
            + AltitudeBaseCentimeters;
        return (uint)Math.Clamp(centimeters, 0, uint.MaxValue);
    }

    /// <summary>
    ///     This method writes a big-endian 32-bit value.
    /// </summary>
    private static void WriteUInt32(IBufferWriter<byte> buffer, uint value)
    {
        Span<byte> span = buffer.GetSpan(4);
        BinaryPrimitives.WriteUInt32BigEndian(span, value);
        buffer.Advance(4);
    }
}
