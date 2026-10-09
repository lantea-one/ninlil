using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations;

/// <inheritdoc />
public partial class AddDnsRewriteClientScoping : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ClientGroupId",
            table: "NinlilDnsRewrites",
            type: "uuid",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "ClientId",
            table: "NinlilDnsRewrites",
            type: "uuid",
            nullable: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsRewrites_ClientGroupId",
            table: "NinlilDnsRewrites",
            column: "ClientGroupId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsRewrites_ClientId",
            table: "NinlilDnsRewrites",
            column: "ClientId"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsRewrites_ClientGroupId",
            table: "NinlilDnsRewrites"
        );

        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsRewrites_ClientId",
            table: "NinlilDnsRewrites"
        );

        migrationBuilder.DropColumn(name: "ClientGroupId", table: "NinlilDnsRewrites");

        migrationBuilder.DropColumn(name: "ClientId", table: "NinlilDnsRewrites");
    }
}
