using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddDhcpScopeCustomOptionsAndTagRules : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NinlilDhcpScopeCustomOptions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ScopeId = table.Column<Guid>(type: "TEXT", nullable: false),
                OptionTag = table.Column<byte>(type: "INTEGER", nullable: false),
                ValueHex = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                Comments = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                OriginatingNodeId = table.Column<Guid>(type: "TEXT", nullable: true),
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
        );

        migrationBuilder.CreateTable(
            name: "NinlilDhcpTagRules",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                ScopeId = table.Column<Guid>(type: "TEXT", nullable: true),
                Priority = table.Column<int>(type: "INTEGER", nullable: false),
                IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                VendorClassIdentifierMatch = table.Column<string>(
                    type: "TEXT",
                    maxLength: 256,
                    nullable: true
                ),
                VendorClassIdentifierMatchMode = table.Column<string>(
                    type: "TEXT",
                    maxLength: 16,
                    nullable: false
                ),
                UserClassMatch = table.Column<string>(
                    type: "TEXT",
                    maxLength: 256,
                    nullable: true
                ),
                UserClassMatchMode = table.Column<string>(
                    type: "TEXT",
                    maxLength: 16,
                    nullable: false
                ),
                RelayAgentCircuitIdHex = table.Column<string>(
                    type: "TEXT",
                    maxLength: 512,
                    nullable: true
                ),
                RelayAgentRemoteIdHex = table.Column<string>(
                    type: "TEXT",
                    maxLength: 512,
                    nullable: true
                ),
                Comments = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                OriginatingNodeId = table.Column<Guid>(type: "TEXT", nullable: true),
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
        );

        migrationBuilder.CreateTable(
            name: "NinlilDhcpTagRuleOptions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TagRuleId = table.Column<Guid>(type: "TEXT", nullable: false),
                OptionTag = table.Column<byte>(type: "INTEGER", nullable: false),
                ValueHex = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                OriginatingNodeId = table.Column<Guid>(type: "TEXT", nullable: true),
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
        );

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
