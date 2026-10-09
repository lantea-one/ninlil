using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class QueryPerformanceIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsQueryLogEntries_ClientAddress",
            table: "NinlilDnsQueryLogEntries");

        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsQueryLogEntries_ClientIdentifier",
            table: "NinlilDnsQueryLogEntries");

        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsQueryLogEntries_QueriedName",
            table: "NinlilDnsQueryLogEntries");

        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsQueryLogEntries_ResponseCode",
            table: "NinlilDnsQueryLogEntries");

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsRecords_ManagedByFeature_Type",
            table: "NinlilDnsRecords",
            columns: new[] { "ManagedByFeature", "Type" });

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsQueryLogEntries_ClientAddress_OccurredAtUtc",
            table: "NinlilDnsQueryLogEntries",
            columns: new[] { "ClientAddress", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsQueryLogEntries_ClientIdentifier_OccurredAtUtc",
            table: "NinlilDnsQueryLogEntries",
            columns: new[] { "ClientIdentifier", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsQueryLogEntries_ResponseCode_OccurredAtUtc",
            table: "NinlilDnsQueryLogEntries",
            columns: new[] { "ResponseCode", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDhcpLeases_State_LeaseExpiresAtUtc",
            table: "NinlilDhcpLeases",
            columns: new[] { "State", "LeaseExpiresAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsRecords_ManagedByFeature_Type",
            table: "NinlilDnsRecords");

        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsQueryLogEntries_ClientAddress_OccurredAtUtc",
            table: "NinlilDnsQueryLogEntries");

        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsQueryLogEntries_ClientIdentifier_OccurredAtUtc",
            table: "NinlilDnsQueryLogEntries");

        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsQueryLogEntries_ResponseCode_OccurredAtUtc",
            table: "NinlilDnsQueryLogEntries");

        migrationBuilder.DropIndex(
            name: "IX_NinlilDhcpLeases_State_LeaseExpiresAtUtc",
            table: "NinlilDhcpLeases");

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsQueryLogEntries_ClientAddress",
            table: "NinlilDnsQueryLogEntries",
            column: "ClientAddress");

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsQueryLogEntries_ClientIdentifier",
            table: "NinlilDnsQueryLogEntries",
            column: "ClientIdentifier");

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsQueryLogEntries_QueriedName",
            table: "NinlilDnsQueryLogEntries",
            column: "QueriedName");

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsQueryLogEntries_ResponseCode",
            table: "NinlilDnsQueryLogEntries",
            column: "ResponseCode");
    }
}
