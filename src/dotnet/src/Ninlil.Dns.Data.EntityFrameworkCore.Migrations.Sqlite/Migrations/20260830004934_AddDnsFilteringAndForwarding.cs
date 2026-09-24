using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddDnsFilteringAndForwarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NinlilDnsClientGroupListAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ListId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsClientGroupListAssignments", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "NinlilDnsClientGroupNetworks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Cidr = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsClientGroupNetworks", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "NinlilDnsClientGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsClientGroups", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "NinlilDnsFilterListEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ListId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsFilterListEntries", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "NinlilDnsFilterLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SubscriptionUrl = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2048,
                        nullable: true
                    ),
                    RefreshInterval = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    LastFetchedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Comments = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsFilterLists", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "NinlilDnsForwarders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DefinitionString = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2048,
                        nullable: false
                    ),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Comments = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsForwarders", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsClientGroupListAssignments_ClientGroupId",
                table: "NinlilDnsClientGroupListAssignments",
                column: "ClientGroupId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsClientGroupListAssignments_ListId",
                table: "NinlilDnsClientGroupListAssignments",
                column: "ListId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsClientGroupNetworks_ClientGroupId",
                table: "NinlilDnsClientGroupNetworks",
                column: "ClientGroupId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsClientGroups_Name",
                table: "NinlilDnsClientGroups",
                column: "Name",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsFilterListEntries_ListId",
                table: "NinlilDnsFilterListEntries",
                column: "ListId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsFilterLists_Name",
                table: "NinlilDnsFilterLists",
                column: "Name",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsForwarders_Name",
                table: "NinlilDnsForwarders",
                column: "Name",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NinlilDnsClientGroupListAssignments");

            migrationBuilder.DropTable(name: "NinlilDnsClientGroupNetworks");

            migrationBuilder.DropTable(name: "NinlilDnsClientGroups");

            migrationBuilder.DropTable(name: "NinlilDnsFilterListEntries");

            migrationBuilder.DropTable(name: "NinlilDnsFilterLists");

            migrationBuilder.DropTable(name: "NinlilDnsForwarders");
        }
    }
}
