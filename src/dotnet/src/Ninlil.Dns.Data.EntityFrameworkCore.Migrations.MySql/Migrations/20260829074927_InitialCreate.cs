using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase().Annotation("MySql:CharSet", "utf8mb4");

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
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsLeases", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    TimestampUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    Severity = table
                        .Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Category = table
                        .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Message = table
                        .Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Exception = table
                        .Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PropertiesJson = table
                        .Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsLogs", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    TimestampUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    Category = table
                        .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<double>(type: "double", nullable: false),
                    DimensionsJson = table
                        .Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsMetrics", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsRoles",
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
                    NormalizedName = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table
                        .Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table
                        .Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsRoles", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    UserName = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedUserName = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayName = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedEmail = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmailConfirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PasswordHash = table
                        .Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SecurityStamp = table
                        .Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table
                        .Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PhoneNumber = table
                        .Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PhoneNumberConfirmed = table.Column<bool>(
                        type: "tinyint(1)",
                        nullable: false
                    ),
                    TwoFactorEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TwoFactorTotpSecretEncrypted = table
                        .Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LockoutEndUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    LockoutEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false),
                    IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsUsers", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsZones",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    Name = table
                        .Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table
                        .Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDnssecSigned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DnssecAlgorithm = table
                        .Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PrimaryServerAddresses = table
                        .Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ForwarderAddresses = table
                        .Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
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
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsZones", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsRoleClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    RoleId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    ClaimType = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimValue = table
                        .Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NinlilDnsRoleClaims_NinlilDnsRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "NinlilDnsRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsApiTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    UserId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    Name = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TokenHash = table
                        .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table
                        .Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    RevokedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsApiTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NinlilDnsApiTokens_NinlilDnsUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "NinlilDnsUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    UserId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    RefreshTokenHash = table
                        .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    RevokedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    IpAddress = table
                        .Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserAgent = table
                        .Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NinlilDnsSessions_NinlilDnsUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "NinlilDnsUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsUserClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    UserId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    ClaimType = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimValue = table
                        .Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NinlilDnsUserClaims_NinlilDnsUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "NinlilDnsUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    RoleId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_NinlilDnsUserRoles_NinlilDnsRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "NinlilDnsRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_NinlilDnsUserRoles_NinlilDnsUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "NinlilDnsUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    ZoneId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    Name = table
                        .Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table
                        .Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Class = table
                        .Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TimeToLiveSeconds = table.Column<long>(type: "bigint", nullable: false),
                    DataJson = table
                        .Column<string>(type: "longtext", nullable: false)
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
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NinlilDnsRecords_NinlilDnsZones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "NinlilDnsZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsApiTokens_TokenHash",
            table: "NinlilDnsApiTokens",
            column: "TokenHash",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsApiTokens_UserId",
            table: "NinlilDnsApiTokens",
            column: "UserId"
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

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsLogs_TimestampUtc_Severity",
            table: "NinlilDnsLogs",
            columns: new[] { "TimestampUtc", "Severity" }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsMetrics_Category_Name_TimestampUtc",
            table: "NinlilDnsMetrics",
            columns: new[] { "Category", "Name", "TimestampUtc" }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsRecords_ZoneId_Name_Type",
            table: "NinlilDnsRecords",
            columns: new[] { "ZoneId", "Name", "Type" }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsRoleClaims_RoleId",
            table: "NinlilDnsRoleClaims",
            column: "RoleId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsRoles_NormalizedName",
            table: "NinlilDnsRoles",
            column: "NormalizedName",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsSessions_RefreshTokenHash",
            table: "NinlilDnsSessions",
            column: "RefreshTokenHash",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsSessions_UserId",
            table: "NinlilDnsSessions",
            column: "UserId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsUserClaims_UserId",
            table: "NinlilDnsUserClaims",
            column: "UserId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsUserRoles_RoleId",
            table: "NinlilDnsUserRoles",
            column: "RoleId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsUsers_NormalizedEmail",
            table: "NinlilDnsUsers",
            column: "NormalizedEmail"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsUsers_NormalizedUserName",
            table: "NinlilDnsUsers",
            column: "NormalizedUserName",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsZones_Name",
            table: "NinlilDnsZones",
            column: "Name",
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDnsApiTokens");

        migrationBuilder.DropTable(name: "NinlilDnsLeases");

        migrationBuilder.DropTable(name: "NinlilDnsLogs");

        migrationBuilder.DropTable(name: "NinlilDnsMetrics");

        migrationBuilder.DropTable(name: "NinlilDnsRecords");

        migrationBuilder.DropTable(name: "NinlilDnsRoleClaims");

        migrationBuilder.DropTable(name: "NinlilDnsSessions");

        migrationBuilder.DropTable(name: "NinlilDnsUserClaims");

        migrationBuilder.DropTable(name: "NinlilDnsUserRoles");

        migrationBuilder.DropTable(name: "NinlilDnsZones");

        migrationBuilder.DropTable(name: "NinlilDnsRoles");

        migrationBuilder.DropTable(name: "NinlilDnsUsers");
    }
}
