using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PetStar.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TacticalDuel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_duel",
                table: "battles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "duel_decks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    entries_json = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_duel_decks", x => x.id);
                    table.ForeignKey(
                        name: "fk_duel_decks_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_battles_is_duel_status_round_deadline",
                table: "battles",
                columns: new[] { "is_duel", "status", "round_deadline" });

            migrationBuilder.CreateIndex(
                name: "ix_duel_decks_membership_id_is_default",
                table: "duel_decks",
                columns: new[] { "membership_id", "is_default" });

            migrationBuilder.CreateIndex(
                name: "ix_duel_decks_membership_id_server_id",
                table: "duel_decks",
                columns: new[] { "membership_id", "server_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "duel_decks");

            migrationBuilder.DropIndex(
                name: "ix_battles_is_duel_status_round_deadline",
                table: "battles");

            migrationBuilder.DropColumn(
                name: "is_duel",
                table: "battles");
        }
    }
}
