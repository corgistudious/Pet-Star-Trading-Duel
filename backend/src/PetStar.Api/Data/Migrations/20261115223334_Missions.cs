using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetStar.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Missions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mission_claims",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    mission_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    period_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mission_claims", x => new { x.membership_id, x.mission_id, x.period_key });
                    table.ForeignKey(
                        name: "fk_mission_claims_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mission_claims_membership_id_server_id",
                table: "mission_claims",
                columns: new[] { "membership_id", "server_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mission_claims");
        }
    }
}
