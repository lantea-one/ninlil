using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddDhcpScopeCustomOptionsAndTagRules : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder
            .CreateTable(
                name: "NinlilDhcpScopeCustomOptions",
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
                    OptionTag = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ValueHex = table
                        .Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false)
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
                    table.PrimaryKey("PK_NinlilDhcpScopeCustomOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NinlilDhcpScopeCustomOptions_NinlilDhcpScopes_ScopeId",
                        column: x => x.ScopeId,
                        principalTable: "NinlilDhcpScopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDhcpTagRules",
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
                    ScopeId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: true,
                        collation: "ascii_general_ci"
                    ),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    VendorClassIdentifierMatch = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VendorClassIdentifierMatchMode = table
                        .Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserClassMatch = table
                        .Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserClassMatchMode = table
                        .Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RelayAgentCircuitIdHex = table
                        .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RelayAgentRemoteIdHex = table
                        .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
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
                    OriginatingNodeId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: true,
                        collation: "ascii_general_ci"
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDhcpTagRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NinlilDhcpTagRules_NinlilDhcpScopes_ScopeId",
                        column: x => x.ScopeId,
                        principalTable: "NinlilDhcpScopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDhcpTagRuleOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    TagRuleId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    OptionTag = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ValueHex = table
                        .Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false)
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
                    table.PrimaryKey("PK_NinlilDhcpTagRuleOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NinlilDhcpTagRuleOptions_NinlilDhcpTagRules_TagRuleId",
                        column: x => x.TagRuleId,
                        principalTable: "NinlilDhcpTagRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDhcpScopeCustomOptions_ScopeId_OptionTag",
            table: "NinlilDhcpScopeCustomOptions",
            columns: new[] { "ScopeId", "OptionTag" },
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDhcpTagRuleOptions_TagRuleId_OptionTag",
            table: "NinlilDhcpTagRuleOptions",
            columns: new[] { "TagRuleId", "OptionTag" },
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDhcpTagRules_ScopeId",
            table: "NinlilDhcpTagRules",
            column: "ScopeId"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDhcpScopeCustomOptions");

        migrationBuilder.DropTable(name: "NinlilDhcpTagRuleOptions");

        migrationBuilder.DropTable(name: "NinlilDhcpTagRules");
    }
}
