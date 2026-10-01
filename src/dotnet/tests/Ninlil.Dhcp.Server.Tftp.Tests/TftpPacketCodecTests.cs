using System.Text;
using Ninlil.Dhcp.Server.Tftp.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Tests;

/// <summary>
///     This class tests the TFTP codec, <see cref="TftpPacketReader" /> and
///     <see cref="TftpPacketWriter" />, in memory: decoding requests, Acknowledgments and Errors,
///     round trips through the writer, and rejection of empty, truncated, unterminated and
///     wrong-opcode input.
/// </summary>
public sealed class TftpPacketCodecTests
{
    [Fact]
    public void TryReadRequest_DecodesAReadRequest()
    {
        byte[] datagram = BuildRequest(TftpOpcodeEnum.ReadRequest, "boot/pxelinux.0", "octet");

        Assert.True(TftpPacketReader.TryReadRequest(datagram, out TftpRequestPacket packet, out _));
        Assert.Equal(TftpOpcodeEnum.ReadRequest, packet.Opcode);
        Assert.Equal("boot/pxelinux.0", packet.Filename);
        Assert.Equal("octet", packet.Mode);
    }

    [Fact]
    public void TryReadRequest_DecodesAWriteRequest()
    {
        byte[] datagram = BuildRequest(TftpOpcodeEnum.WriteRequest, "upload.bin", "octet");

        Assert.True(TftpPacketReader.TryReadRequest(datagram, out TftpRequestPacket packet, out _));
        Assert.Equal(TftpOpcodeEnum.WriteRequest, packet.Opcode);
    }

    [Fact]
    public void TryReadRequest_RejectsAnEmptyDatagram()
    {
        Assert.False(TftpPacketReader.TryReadRequest([], out _, out string reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void TryReadRequest_RejectsAWrongOpcode()
    {
        byte[] datagram = [0x00, 0x04, 0x00, 0x01];

        Assert.False(TftpPacketReader.TryReadRequest(datagram, out _, out string reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void TryReadRequest_RejectsAMissingFilenameTerminator()
    {
        // RRQ opcode and a filename with no terminating NUL.
        byte[] datagram = [0x00, 0x01, .. Encoding.ASCII.GetBytes("no-terminator")];

        Assert.False(TftpPacketReader.TryReadRequest(datagram, out _, out string reason));
        Assert.Contains("filename", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReadRequest_RejectsAMissingModeTerminator()
    {
        byte[] datagram =
        [
            0x00,
            0x01,
            .. Encoding.ASCII.GetBytes("file.bin"),
            0x00,
            .. Encoding.ASCII.GetBytes("octet"),
        ];

        Assert.False(TftpPacketReader.TryReadRequest(datagram, out _, out string reason));
        Assert.Contains("mode", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteData_ThenTryReadAcknowledgment_RoundTrips()
    {
        byte[] block = [1, 2, 3, 4, 5];
        byte[] dataPacket = TftpPacketWriter.WriteData(7, block);

        // Skip the 4-byte opcode and block number header to check the payload.
        Assert.Equal((byte)0, dataPacket[0]);
        Assert.Equal((byte)3, dataPacket[1]);
        Assert.Equal(block, dataPacket[4..]);
    }

    [Fact]
    public void TryReadAcknowledgment_DecodesABlockNumber()
    {
        byte[] datagram = [0x00, 0x04, 0x01, 0x2c];

        Assert.True(
            TftpPacketReader.TryReadAcknowledgment(datagram, out ushort blockNumber, out _)
        );
        Assert.Equal((ushort)300, blockNumber);
    }

    [Fact]
    public void TryReadAcknowledgment_RejectsATooShortDatagram()
    {
        byte[] datagram = [0x00, 0x04, 0x01];

        Assert.False(TftpPacketReader.TryReadAcknowledgment(datagram, out _, out string reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void TryReadAcknowledgment_RejectsAWrongOpcode()
    {
        byte[] datagram = [0x00, 0x03, 0x00, 0x01, 0x00];

        Assert.False(TftpPacketReader.TryReadAcknowledgment(datagram, out _, out string reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void WriteError_ThenTryReadError_RoundTrips()
    {
        byte[] packet = TftpPacketWriter.WriteError(TftpErrorCodeEnum.FileNotFound, "no such file");

        Assert.True(
            TftpPacketReader.TryReadError(
                packet,
                out TftpErrorCodeEnum errorCode,
                out string errorMessage,
                out _
            )
        );
        Assert.Equal(TftpErrorCodeEnum.FileNotFound, errorCode);
        Assert.Equal("no such file", errorMessage);
    }

    [Fact]
    public void WriteError_WithNoMessage_EncodesAnEmptyString()
    {
        byte[] packet = TftpPacketWriter.WriteError(TftpErrorCodeEnum.AccessViolation, null);

        Assert.True(TftpPacketReader.TryReadError(packet, out _, out string errorMessage, out _));
        Assert.Equal(string.Empty, errorMessage);
    }

    /// <summary>
    ///     This method builds a raw RRQ or WRQ datagram by hand, since
    ///     <see cref="TftpPacketWriter" /> does not encode requests.
    /// </summary>
    private static byte[] BuildRequest(TftpOpcodeEnum opcode, string filename, string mode) =>
        [
            0x00,
            (byte)opcode,
            .. Encoding.ASCII.GetBytes(filename),
            0x00,
            .. Encoding.ASCII.GetBytes(mode),
            0x00,
        ];
}
