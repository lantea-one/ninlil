using System.Net;
using Ninlil.Dns.Server.ClientIdentity.Presence;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests;

/// <summary>
///     This class tests the source-address cap of <see cref="ClientPresenceTracker" /> with the
///     tracker alone, no database: addresses over <see cref="ClientPresenceTracker.MaxSourceAddressesPerFlush" />
///     are counted and not stored, a stored address keeps updating when the buffer is full, and
///     draining resets both the buffer and the count. The flush service's warning is covered in
///     <see cref="ClientPresenceFlushServiceTests" />.
/// </summary>
public sealed class ClientPresenceTrackerTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     This method returns the n-th distinct IPv4 address of 10.0.0.0/8 and beyond.
    /// </summary>
    private static IPAddress AddressNumber(int number) =>
        new([10, (byte)(number >> 16), (byte)(number >> 8), (byte)number]);

    private static ClientPresenceTracker FullTracker()
    {
        ClientPresenceTracker tracker = new();
        for (int i = 0; i < ClientPresenceTracker.MaxSourceAddressesPerFlush; i++)
            tracker.RecordSourceAddress(AddressNumber(i), Now);

        return tracker;
    }

    [Fact]
    public void RecordSourceAddress_OverTheCap_CountsTheExtraAddressesAndDoesNotStoreThem()
    {
        ClientPresenceTracker tracker = FullTracker();

        for (int i = 0; i < 10; i++)
            tracker.RecordSourceAddress(
                AddressNumber(ClientPresenceTracker.MaxSourceAddressesPerFlush + i),
                Now
            );

        ClientPresenceSnapshot snapshot = tracker.Drain();

        Assert.Equal(
            ClientPresenceTracker.MaxSourceAddressesPerFlush,
            snapshot.SourceAddresses.Count
        );
        Assert.Equal(10, snapshot.DroppedSourceAddresses);
    }

    [Fact]
    public void RecordSourceAddress_WhenFull_StillUpdatesAnAddressAlreadyInTheBuffer()
    {
        ClientPresenceTracker tracker = FullTracker();
        DateTimeOffset later = Now.AddMinutes(1);

        tracker.RecordSourceAddress(AddressNumber(5), later);
        ClientPresenceSnapshot snapshot = tracker.Drain();

        Assert.Equal(later, snapshot.SourceAddresses[AddressNumber(5)].SeenAtUtc);
        Assert.Equal(0, snapshot.DroppedSourceAddresses);
    }

    [Fact]
    public void Drain_ResetsTheBufferAndTheDropCount()
    {
        ClientPresenceTracker tracker = FullTracker();
        tracker.RecordSourceAddress(
            AddressNumber(ClientPresenceTracker.MaxSourceAddressesPerFlush),
            Now
        );
        tracker.Drain();

        tracker.RecordSourceAddress(
            AddressNumber(ClientPresenceTracker.MaxSourceAddressesPerFlush),
            Now
        );
        ClientPresenceSnapshot snapshot = tracker.Drain();

        Assert.Single(snapshot.SourceAddresses);
        Assert.Equal(0, snapshot.DroppedSourceAddresses);
    }

    [Fact]
    public void RecordSourceAddress_AnIPv4MappedAndPlainFormOfOneAddress_CountAsOneEntry()
    {
        ClientPresenceTracker tracker = new();
        IPAddress address = IPAddress.Parse("192.0.2.9");

        tracker.RecordSourceAddress(address, Now);
        tracker.RecordSourceAddress(address.MapToIPv6(), Now);

        Assert.Single(tracker.Drain().SourceAddresses);
    }
}
