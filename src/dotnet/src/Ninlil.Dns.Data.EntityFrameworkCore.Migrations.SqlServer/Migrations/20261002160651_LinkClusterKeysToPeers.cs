using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

/// <inheritdoc />
public partial class LinkClusterKeysToPeers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ClusterInviteExpiresAtUtc",
            table: "NinlilDnsTsigKeys",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ClusterNodeId",
            table: "NinlilDnsTsigKeys",
            type: "uniqueidentifier",
            nullable: true);

        // Link every existing peer's key to its peer, which until now referred to the key
        // only by name, so clusters keep working after the upgrade. This runs once, here:
        // a key created later by name alone must never be linked automatically. Unused
        // invite keys from before the upgrade stay unlinked and no longer authenticate.
        migrationBuilder.Sql(
            """
            UPDATE [NinlilDnsTsigKeys] SET [ClusterNodeId] = (SELECT TOP 1 [Id] FROM [NinlilDnsClusterNodes] WHERE [NinlilDnsClusterNodes].[TsigKeyName] = [NinlilDnsTsigKeys].[Name]);
            """
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ClusterInviteExpiresAtUtc",
            table: "NinlilDnsTsigKeys");

        migrationBuilder.DropColumn(
            name: "ClusterNodeId",
            table: "NinlilDnsTsigKeys");
    }
}
