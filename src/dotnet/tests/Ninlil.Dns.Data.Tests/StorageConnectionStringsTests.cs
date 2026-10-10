using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Topology;

// Define our namespace.
namespace Ninlil.Dns.Data.Tests;

/// <summary>
///     These tests cover <see cref="StorageConnectionStrings" />, the singleton that the storage
///     switch applier updates in place after a switch. They use in-memory values only.
/// </summary>
public sealed class StorageConnectionStringsTests
{
    [Fact]
    public void Replace_MakesTheSingletonDescribeTheNewStrings()
    {
        StorageConnectionStrings strings = new(
            StorageEngineEnum.Sqlite,
            "Data Source=a",
            "Data Source=a",
            null,
            []
        );
        StorageOptions options = new()
        {
            Engine = StorageEngineEnum.PostgreSql,
            ConnectionString = "Host=b",
        };
        Assert.False(strings.IsComposedFrom(options));

        strings.Replace(
            new StorageConnectionStrings(
                StorageEngineEnum.PostgreSql,
                "Host=b",
                "Host=b;Target Session Attributes=read-write",
                "Host=r",
                ["w"]
            )
        );

        Assert.True(strings.IsComposedFrom(options));
        Assert.Equal(StorageEngineEnum.PostgreSql, strings.Engine);
        Assert.Equal("Host=b;Target Session Attributes=read-write", strings.Primary);
        Assert.Equal("Host=r", strings.Replica);
        Assert.Equal(["w"], strings.Warnings);
    }
}
