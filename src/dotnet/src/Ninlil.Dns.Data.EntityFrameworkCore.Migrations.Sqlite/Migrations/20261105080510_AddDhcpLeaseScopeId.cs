using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddDhcpLeaseScopeId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NinlilDhcpLeases_ScopeName_ClientIdentifier",
            table: "NinlilDhcpLeases");

        migrationBuilder.DropIndex(
            name: "IX_NinlilDhcpLeases_ScopeName_IpAddress",
            table: "NinlilDhcpLeases");

        migrationBuilder.AddColumn<Guid>(
            name: "ScopeId",
            table: "NinlilDhcpLeases",
            type: "TEXT",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        // The new column holds the empty identifier on every existing row. A lease took its
        // scope by name until now, so the scope with that name supplies the identifier. Scope
        // names are unique, so the subquery returns at most one row.
        migrationBuilder.Sql(
            """
            UPDATE "NinlilDhcpLeases"
            SET "ScopeId" = (
                SELECT scope."Id" FROM "NinlilDhcpScopes" scope WHERE scope."Name" = "NinlilDhcpLeases"."ScopeName"
            )
            WHERE EXISTS (
                SELECT 1 FROM "NinlilDhcpScopes" scope WHERE scope."Name" = "NinlilDhcpLeases"."ScopeName"
            );
            """
        );

        // A lease whose scope name matches no scope belongs to a scope that was deleted. It
        // keeps the empty identifier, and no DHCP engine, DNS registration or API view can use
        // it. Deleting the row also keeps the unique indexes below from seeing the empty
        // identifier repeat across the orphaned rows of different deleted scopes.
        migrationBuilder.Sql(
            """
            DELETE FROM "NinlilDhcpLeases"
            WHERE "ScopeId" = '00000000-0000-0000-0000-000000000000';
            """
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDhcpLeases_ScopeId_ClientIdentifier",
            table: "NinlilDhcpLeases",
            columns: new[] { "ScopeId", "ClientIdentifier" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDhcpLeases_ScopeId_IpAddress",
            table: "NinlilDhcpLeases",
            columns: new[] { "ScopeId", "IpAddress" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDhcpLeases_ScopeName_ClientIdentifier",
            table: "NinlilDhcpLeases",
            columns: new[] { "ScopeName", "ClientIdentifier" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NinlilDhcpLeases_ScopeId_ClientIdentifier",
            table: "NinlilDhcpLeases");

        migrationBuilder.DropIndex(
            name: "IX_NinlilDhcpLeases_ScopeId_IpAddress",
            table: "NinlilDhcpLeases");

        migrationBuilder.DropIndex(
            name: "IX_NinlilDhcpLeases_ScopeName_ClientIdentifier",
            table: "NinlilDhcpLeases");

        migrationBuilder.DropColumn(
            name: "ScopeId",
            table: "NinlilDhcpLeases");

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDhcpLeases_ScopeName_ClientIdentifier",
            table: "NinlilDhcpLeases",
            columns: new[] { "ScopeName", "ClientIdentifier" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDhcpLeases_ScopeName_IpAddress",
            table: "NinlilDhcpLeases",
            columns: new[] { "ScopeName", "IpAddress" },
            unique: true);
    }
}
