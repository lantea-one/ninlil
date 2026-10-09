using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations;

/// <inheritdoc />
public partial class AddDhcpScopesReservationsAndRenameLeaseTable : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDnsLeases");

        migrationBuilder.CreateTable(
            name: "NinlilDhcpLeases",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProtocolVersion = table.Column<string>(
                    type: "character varying(16)",
                    maxLength: 16,
                    nullable: false
                ),
                ScopeName = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                ClientIdentifier = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                IpAddress = table.Column<string>(
                    type: "character varying(45)",
                    maxLength: 45,
                    nullable: false
                ),
                HostName = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true
                ),
                State = table.Column<string>(
                    type: "character varying(16)",
                    maxLength: 16,
                    nullable: false
                ),
                LeaseObtainedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                LeaseExpiresAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                UpdatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                OriginatingNodeId = table.Column<Guid>(type: "uuid", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDhcpLeases", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "NinlilDhcpScopes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                ProtocolVersion = table.Column<string>(
                    type: "character varying(16)",
                    maxLength: 16,
                    nullable: false
                ),
                RangeStartAddress = table.Column<string>(
                    type: "character varying(45)",
                    maxLength: 45,
                    nullable: false
                ),
                RangeEndAddress = table.Column<string>(
                    type: "character varying(45)",
                    maxLength: 45,
                    nullable: false
                ),
                SubnetPrefixLength = table.Column<int>(type: "integer", nullable: false),
                DefaultLeaseDuration = table.Column<TimeSpan>(
                    type: "interval",
                    nullable: false
                ),
                MaxLeaseDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                IsDisabled = table.Column<bool>(type: "boolean", nullable: false),
                Comments = table.Column<string>(
                    type: "character varying(4096)",
                    maxLength: 4096,
                    nullable: true
                ),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                UpdatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                OriginatingNodeId = table.Column<Guid>(type: "uuid", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDhcpScopes", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "NinlilDhcpReservations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                ClientIdentifier = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                IpAddress = table.Column<string>(
                    type: "character varying(45)",
                    maxLength: 45,
                    nullable: false
                ),
                HostName = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true
                ),
                IsDisabled = table.Column<bool>(type: "boolean", nullable: false),
                Comments = table.Column<string>(
                    type: "character varying(4096)",
                    maxLength: 4096,
                    nullable: true
                ),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                UpdatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                OriginatingNodeId = table.Column<Guid>(type: "uuid", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDhcpReservations", x => x.Id);
                table.ForeignKey(
                    name: "FK_NinlilDhcpReservations_NinlilDhcpScopes_ScopeId",
                    column: x => x.ScopeId,
                    principalTable: "NinlilDhcpScopes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDhcpLeases_ScopeName_ClientIdentifier",
            table: "NinlilDhcpLeases",
            columns: new[] { "ScopeName", "ClientIdentifier" },
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDhcpLeases_ScopeName_IpAddress",
            table: "NinlilDhcpLeases",
            columns: new[] { "ScopeName", "IpAddress" },
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDhcpReservations_ScopeId_ClientIdentifier",
            table: "NinlilDhcpReservations",
            columns: new[] { "ScopeId", "ClientIdentifier" },
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDhcpReservations_ScopeId_IpAddress",
            table: "NinlilDhcpReservations",
            columns: new[] { "ScopeId", "IpAddress" },
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDhcpScopes_Name",
            table: "NinlilDhcpScopes",
            column: "Name",
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDhcpLeases");

        migrationBuilder.DropTable(name: "NinlilDhcpReservations");

        migrationBuilder.DropTable(name: "NinlilDhcpScopes");

        migrationBuilder.CreateTable(
            name: "NinlilDnsLeases",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClientIdentifier = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                HostName = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true
                ),
                IpAddress = table.Column<string>(
                    type: "character varying(45)",
                    maxLength: 45,
                    nullable: false
                ),
                LeaseExpiresAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                LeaseObtainedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                OriginatingNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                ProtocolVersion = table.Column<string>(
                    type: "character varying(16)",
                    maxLength: 16,
                    nullable: false
                ),
                ScopeName = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                State = table.Column<string>(
                    type: "character varying(16)",
                    maxLength: 16,
                    nullable: false
                ),
                UpdatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsLeases", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsLeases_ScopeName_ClientIdentifier",
            table: "NinlilDnsLeases",
            columns: new[] { "ScopeName", "ClientIdentifier" },
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsLeases_ScopeName_IpAddress",
            table: "NinlilDnsLeases",
            columns: new[] { "ScopeName", "IpAddress" },
            unique: true
        );
    }
}
