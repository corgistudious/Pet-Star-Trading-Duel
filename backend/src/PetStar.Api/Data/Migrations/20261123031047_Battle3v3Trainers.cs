using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PetStar.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Battle3v3Trainers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The 20-card Arena (retired) was never deployed: its deck tables and queue rows hold no player data.
            // Clear stale queue rows so the queue can point at 3v3 teams instead of Arena decks.
            migrationBuilder.Sql("DELETE FROM arena_queue;");
            migrationBuilder.DropForeignKey(
                name: "fk_arena_queue_arena_decks_deck_id",
                table: "arena_queue");

            migrationBuilder.DropTable(
                name: "arena_deck_cards");

            migrationBuilder.DropTable(
                name: "arena_decks");

            migrationBuilder.RenameColumn(
                name: "deck_id",
                table: "arena_queue",
                newName: "lineup_id");

            migrationBuilder.RenameIndex(
                name: "ix_arena_queue_deck_id",
                table: "arena_queue",
                newName: "ix_arena_queue_lineup_id");

            migrationBuilder.CreateTable(
                name: "team_lineups",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    card1id = table.Column<long>(type: "bigint", nullable: false),
                    card2id = table.Column<long>(type: "bigint", nullable: false),
                    card3id = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_lineups", x => x.id);
                    table.CheckConstraint("ck_team_lineup_distinct", "card1id <> card2id AND card1id <> card3id AND card2id <> card3id");
                    table.ForeignKey(
                        name: "fk_team_lineups_card_instances_card1id",
                        column: x => x.card1id,
                        principalTable: "card_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_team_lineups_card_instances_card2id",
                        column: x => x.card2id,
                        principalTable: "card_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_team_lineups_card_instances_card3id",
                        column: x => x.card3id,
                        principalTable: "card_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_team_lineups_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trainer_outfits",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    outfit_id = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    acquired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trainer_outfits", x => new { x.membership_id, x.outfit_id });
                    table.ForeignKey(
                        name: "fk_trainer_outfits_server_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "server_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trainer_profiles",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    character_id = table.Column<string>(type: "text", nullable: false),
                    appearance_id = table.Column<string>(type: "text", nullable: false),
                    outfit_id = table.Column<string>(type: "text", nullable: false),
                    companion_card_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trainer_profiles", x => x.membership_id);
                    table.ForeignKey(
                        name: "fk_trainer_profiles_card_instances_companion_card_id",
                        column: x => x.companion_card_id,
                        principalTable: "card_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trainer_profiles_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_team_lineups_card1id",
                table: "team_lineups",
                column: "card1id");

            migrationBuilder.CreateIndex(
                name: "ix_team_lineups_card2id",
                table: "team_lineups",
                column: "card2id");

            migrationBuilder.CreateIndex(
                name: "ix_team_lineups_card3id",
                table: "team_lineups",
                column: "card3id");

            migrationBuilder.CreateIndex(
                name: "ix_team_lineups_membership_id_is_default",
                table: "team_lineups",
                columns: new[] { "membership_id", "is_default" });

            migrationBuilder.CreateIndex(
                name: "ix_team_lineups_membership_id_server_id",
                table: "team_lineups",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_trainer_profiles_companion_card_id",
                table: "trainer_profiles",
                column: "companion_card_id");

            migrationBuilder.CreateIndex(
                name: "ix_trainer_profiles_membership_id_server_id",
                table: "trainer_profiles",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_arena_queue_team_lineups_lineup_id",
                table: "arena_queue",
                column: "lineup_id",
                principalTable: "team_lineups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_arena_queue_team_lineups_lineup_id",
                table: "arena_queue");

            migrationBuilder.DropTable(
                name: "team_lineups");

            migrationBuilder.DropTable(
                name: "trainer_outfits");

            migrationBuilder.DropTable(
                name: "trainer_profiles");

            migrationBuilder.RenameColumn(
                name: "lineup_id",
                table: "arena_queue",
                newName: "deck_id");

            migrationBuilder.RenameIndex(
                name: "ix_arena_queue_lineup_id",
                table: "arena_queue",
                newName: "ix_arena_queue_deck_id");

            migrationBuilder.CreateTable(
                name: "arena_decks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    energy_types = table.Column<string>(type: "text", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arena_decks", x => x.id);
                    table.ForeignKey(
                        name: "fk_arena_decks_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "arena_deck_cards",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    card_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    count = table.Column<int>(type: "integer", nullable: false),
                    deck_id = table.Column<long>(type: "bigint", nullable: false),
                    def_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arena_deck_cards", x => x.id);
                    table.CheckConstraint("ck_arena_deck_count", "count BETWEEN 1 AND 20");
                    table.ForeignKey(
                        name: "fk_arena_deck_cards_arena_decks_deck_id",
                        column: x => x.deck_id,
                        principalTable: "arena_decks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_arena_deck_cards_card_instances_card_instance_id",
                        column: x => x.card_instance_id,
                        principalTable: "card_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_arena_deck_cards_card_instance_id",
                table: "arena_deck_cards",
                column: "card_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_arena_deck_cards_deck_id_card_instance_id",
                table: "arena_deck_cards",
                columns: new[] { "deck_id", "card_instance_id" },
                unique: true,
                filter: "card_instance_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_arena_decks_membership_id_is_default",
                table: "arena_decks",
                columns: new[] { "membership_id", "is_default" });

            migrationBuilder.CreateIndex(
                name: "ix_arena_decks_membership_id_server_id",
                table: "arena_decks",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_arena_queue_arena_decks_deck_id",
                table: "arena_queue",
                column: "deck_id",
                principalTable: "arena_decks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
