using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PetStar.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArenaBattle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "arena_card_versions",
                columns: table => new
                {
                    version = table.Column<string>(type: "text", nullable: false),
                    definitions_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arena_card_versions", x => x.version);
                });

            migrationBuilder.CreateTable(
                name: "arena_decks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    energy_types = table.Column<string>(type: "text", nullable: false),
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
                name: "arena_ratings",
                columns: table => new
                {
                    season_id = table.Column<int>(type: "integer", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    wins = table.Column<int>(type: "integer", nullable: false),
                    losses = table.Column<int>(type: "integer", nullable: false),
                    draws = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arena_ratings", x => new { x.season_id, x.membership_id });
                    table.ForeignKey(
                        name: "fk_arena_ratings_ranked_seasons_season_id",
                        column: x => x.season_id,
                        principalTable: "ranked_seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_arena_ratings_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "arena_matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    defs_version = table.Column<string>(type: "text", nullable: false),
                    seed = table.Column<long>(type: "bigint", nullable: false),
                    stage_id = table.Column<string>(type: "text", nullable: true),
                    room_code = table.Column<string>(type: "text", nullable: true),
                    season_id = table.Column<int>(type: "integer", nullable: true),
                    state_json = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    event_seq = table.Column<int>(type: "integer", nullable: false),
                    deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    winner_side = table.Column<int>(type: "integer", nullable: true),
                    end_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arena_matches", x => x.id);
                    table.ForeignKey(
                        name: "fk_arena_matches_arena_card_versions_defs_version",
                        column: x => x.defs_version,
                        principalTable: "arena_card_versions",
                        principalColumn: "version",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_arena_matches_game_servers_server_id",
                        column: x => x.server_id,
                        principalTable: "game_servers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_arena_matches_ranked_seasons_season_id",
                        column: x => x.season_id,
                        principalTable: "ranked_seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "arena_deck_cards",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    deck_id = table.Column<long>(type: "bigint", nullable: false),
                    def_id = table.Column<string>(type: "text", nullable: false),
                    card_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    count = table.Column<int>(type: "integer", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "arena_queue",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    deck_id = table.Column<long>(type: "bigint", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arena_queue", x => x.membership_id);
                    table.ForeignKey(
                        name: "fk_arena_queue_arena_decks_deck_id",
                        column: x => x.deck_id,
                        principalTable: "arena_decks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_arena_queue_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "arena_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    side = table.Column<int>(type: "integer", nullable: false),
                    action_id = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    action_json = table.Column<string>(type: "jsonb", nullable: false),
                    version_after = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arena_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_arena_events_arena_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "arena_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "arena_match_players",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<int>(type: "integer", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_ai = table.Column<bool>(type: "boolean", nullable: false),
                    deck_json = table.Column<string>(type: "jsonb", nullable: false),
                    rating_before = table.Column<int>(type: "integer", nullable: true),
                    rating_after = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arena_match_players", x => new { x.match_id, x.side });
                    table.CheckConstraint("ck_arena_side", "side IN (0, 1)");
                    table.ForeignKey(
                        name: "fk_arena_match_players_arena_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "arena_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_arena_match_players_server_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "server_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "arena_rewards",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    rewards_json = table.Column<string>(type: "jsonb", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arena_rewards", x => x.id);
                    table.ForeignKey(
                        name: "fk_arena_rewards_arena_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "arena_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_arena_rewards_server_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "server_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "arena_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    state_json = table.Column<string>(type: "jsonb", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arena_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_arena_snapshots_arena_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "arena_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateIndex(
                name: "ix_arena_events_match_id_action_id",
                table: "arena_events",
                columns: new[] { "match_id", "action_id" },
                unique: true,
                filter: "action_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_arena_events_match_id_seq",
                table: "arena_events",
                columns: new[] { "match_id", "seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_arena_match_players_match_id_membership_id",
                table: "arena_match_players",
                columns: new[] { "match_id", "membership_id" },
                unique: true,
                filter: "membership_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_arena_match_players_membership_id",
                table: "arena_match_players",
                column: "membership_id");

            migrationBuilder.CreateIndex(
                name: "ix_arena_matches_defs_version",
                table: "arena_matches",
                column: "defs_version");

            migrationBuilder.CreateIndex(
                name: "ix_arena_matches_room_code",
                table: "arena_matches",
                column: "room_code",
                unique: true,
                filter: "status = 'waiting'");

            migrationBuilder.CreateIndex(
                name: "ix_arena_matches_season_id",
                table: "arena_matches",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "ix_arena_matches_server_id",
                table: "arena_matches",
                column: "server_id");

            migrationBuilder.CreateIndex(
                name: "ix_arena_matches_status_deadline",
                table: "arena_matches",
                columns: new[] { "status", "deadline" });

            migrationBuilder.CreateIndex(
                name: "ix_arena_queue_deck_id",
                table: "arena_queue",
                column: "deck_id");

            migrationBuilder.CreateIndex(
                name: "ix_arena_queue_membership_id_server_id",
                table: "arena_queue",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_arena_queue_server_id_mode_enqueued_at",
                table: "arena_queue",
                columns: new[] { "server_id", "mode", "enqueued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_arena_ratings_membership_id_server_id",
                table: "arena_ratings",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_arena_rewards_match_id_membership_id",
                table: "arena_rewards",
                columns: new[] { "match_id", "membership_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_arena_rewards_membership_id",
                table: "arena_rewards",
                column: "membership_id");

            migrationBuilder.CreateIndex(
                name: "ix_arena_snapshots_match_id_seq",
                table: "arena_snapshots",
                columns: new[] { "match_id", "seq" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "arena_deck_cards");

            migrationBuilder.DropTable(
                name: "arena_events");

            migrationBuilder.DropTable(
                name: "arena_match_players");

            migrationBuilder.DropTable(
                name: "arena_queue");

            migrationBuilder.DropTable(
                name: "arena_ratings");

            migrationBuilder.DropTable(
                name: "arena_rewards");

            migrationBuilder.DropTable(
                name: "arena_snapshots");

            migrationBuilder.DropTable(
                name: "arena_decks");

            migrationBuilder.DropTable(
                name: "arena_matches");

            migrationBuilder.DropTable(
                name: "arena_card_versions");
        }
    }
}
