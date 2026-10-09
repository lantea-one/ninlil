using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddDhcpScopesReservationsAndRenameLeaseTable : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDnsLeases");

        migrationBuilder
            .CreateTable(
                name: "NinlilDhcpLeases",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    ProtocolVersion = table
                        .Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScopeName = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientIdentifier = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IpAddress = table
                        .Column<string>(type: "varchar(45)", maxLength: 45, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HostName = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    State = table
                        .Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseObtainedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    OriginatingNodeId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: true,
                        collation: "ascii_general_ci"
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDhcpLeases", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDhcpScopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    Name = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProtocolVersion = table
                        .Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RangeStartAddress = table
                        .Column<string>(type: "varchar(45)", maxLength: 45, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RangeEndAddress = table
                        .Column<string>(type: "varchar(45)", maxLength: 45, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubnetPrefixLength = table.Column<int>(type: "int", nullable: false),
                    DefaultLeaseDuration = table.Column<TimeSpan>(
                        type: "time(6)",
                        nullable: false
                    ),
                    MaxLeaseDuration = table.Column<TimeSpan>(type: "time(6)", nullable: false),
                    IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Comments = table
                        .Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    OriginatingNodeId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: true,
                        collation: "ascii_general_ci"
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDhcpScopes", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDhcpReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    ScopeId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    ClientIdentifier = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IpAddress = table
                        .Column<string>(type: "varchar(45)", maxLength: 45, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HostName = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Comments = table
                        .Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    OriginatingNodeId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: true,
                        collation: "ascii_general_ci"
                    ),
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
            )
            .Annotation("MySql:CharSet", "utf8mb4");

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

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsLeases",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    ClientIdentifier = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    HostName = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IpAddress = table
                        .Column<string>(type: "varchar(45)", maxLength: 45, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    LeaseObtainedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    OriginatingNodeId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: true,
                        collation: "ascii_general_ci"
                    ),
                    ProtocolVersion = table
                        .Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScopeName = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    State = table
                        .Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsLeases", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

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
