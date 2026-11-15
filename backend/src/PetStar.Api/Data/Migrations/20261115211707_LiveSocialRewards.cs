using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PetStar.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LiveSocialRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "guild_id",
                table: "feed_posts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "missed_a",
                table: "battles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "missed_b",
                table: "battles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    author_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_messages_server_memberships_author_membership_id_serve",
                        columns: x => new { x.author_membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "game_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: true),
                    key = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    rewards_json = table.Column<string>(type: "jsonb", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "giftcodes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: true),
                    rewards_json = table.Column<string>(type: "jsonb", nullable: false),
                    max_uses = table.Column<int>(type: "integer", nullable: false),
                    uses = table.Column<int>(type: "integer", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_giftcodes", x => x.id);
                    table.CheckConstraint("ck_giftcode_code", "code ~ '^[A-Z0-9-]{4,32}$'");
                });

            migrationBuilder.CreateTable(
                name: "guilds",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    name_normalized = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    join_policy = table.Column<string>(type: "text", nullable: false),
                    max_members = table.Column<int>(type: "integer", nullable: false),
                    leader_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guilds", x => x.id);
                    table.CheckConstraint("ck_guild_policy", "join_policy IN ('open','approval')");
                    table.ForeignKey(
                        name: "fk_guilds_server_memberships_leader_membership_id_server_id",
                        columns: x => new { x.leader_membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "login_reward_claims",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    streak_day = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_reward_claims", x => new { x.membership_id, x.day });
                });

            migrationBuilder.CreateTable(
                name: "pvp_challenges",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    challenger_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    opponent_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    battle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pvp_challenges", x => x.id);
                    table.ForeignKey(
                        name: "fk_pvp_challenges_server_memberships_challenger_membership_id_",
                        columns: x => new { x.challenger_membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pvp_pending_actions",
                columns: table => new
                {
                    battle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round = table.Column<int>(type: "integer", nullable: false),
                    side = table.Column<int>(type: "integer", nullable: false),
                    actions_json = table.Column<string>(type: "jsonb", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pvp_pending_actions", x => new { x.battle_id, x.round, x.side });
                    table.ForeignKey(
                        name: "fk_pvp_pending_actions_battles_battle_id",
                        column: x => x.battle_id,
                        principalTable: "battles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pvp_queue",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    power = table.Column<int>(type: "integer", nullable: false),
                    enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pvp_queue", x => x.membership_id);
                });

            migrationBuilder.CreateTable(
                name: "event_claims",
                columns: table => new
                {
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_claims", x => new { x.event_id, x.membership_id });
                    table.ForeignKey(
                        name: "fk_event_claims_game_events_event_id",
                        column: x => x.event_id,
                        principalTable: "game_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "giftcode_redemptions",
                columns: table => new
                {
                    giftcode_id = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_giftcode_redemptions", x => new { x.giftcode_id, x.account_id, x.server_id });
                    table.ForeignKey(
                        name: "fk_giftcode_redemptions_giftcodes_giftcode_id",
                        column: x => x.giftcode_id,
                        principalTable: "giftcodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guild_announcements",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    author_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_announcements", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_announcements_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guild_join_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_join_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_join_requests_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guild_members",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_members", x => new { x.guild_id, x.membership_id });
                    table.CheckConstraint("ck_guild_role", "role IN ('leader','officer','member')");
                    table.ForeignKey(
                        name: "fk_guild_members_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_guild_members_server_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "server_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feed_posts_guild_id",
                table: "feed_posts",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_author_membership_id_created_at",
                table: "chat_messages",
                columns: new[] { "author_membership_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_author_membership_id_server_id",
                table: "chat_messages",
                columns: new[] { "author_membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_server_id_channel_id",
                table: "chat_messages",
                columns: new[] { "server_id", "channel", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_game_events_server_id_ends_at",
                table: "game_events",
                columns: new[] { "server_id", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_giftcodes_code",
                table: "giftcodes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guild_announcements_guild_id_created_at",
                table: "guild_announcements",
                columns: new[] { "guild_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_guild_join_requests_guild_id_membership_id",
                table: "guild_join_requests",
                columns: new[] { "guild_id", "membership_id" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_guild_members_membership_id",
                table: "guild_members",
                column: "membership_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guilds_leader_membership_id_server_id",
                table: "guilds",
                columns: new[] { "leader_membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_guilds_server_id_name_normalized",
                table: "guilds",
                columns: new[] { "server_id", "name_normalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pvp_challenges_challenger_membership_id_server_id",
                table: "pvp_challenges",
                columns: new[] { "challenger_membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pvp_challenges_opponent_membership_id_status",
                table: "pvp_challenges",
                columns: new[] { "opponent_membership_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_pvp_queue_server_id_enqueued_at",
                table: "pvp_queue",
                columns: new[] { "server_id", "enqueued_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "event_claims");

            migrationBuilder.DropTable(
                name: "giftcode_redemptions");

            migrationBuilder.DropTable(
                name: "guild_announcements");

            migrationBuilder.DropTable(
                name: "guild_join_requests");

            migrationBuilder.DropTable(
                name: "guild_members");

            migrationBuilder.DropTable(
                name: "login_reward_claims");

            migrationBuilder.DropTable(
                name: "pvp_challenges");

            migrationBuilder.DropTable(
                name: "pvp_pending_actions");

            migrationBuilder.DropTable(
                name: "pvp_queue");

            migrationBuilder.DropTable(
                name: "game_events");

            migrationBuilder.DropTable(
                name: "giftcodes");

            migrationBuilder.DropTable(
                name: "guilds");

            migrationBuilder.DropIndex(
                name: "ix_feed_posts_guild_id",
                table: "feed_posts");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "feed_posts");

            migrationBuilder.DropColumn(
                name: "missed_a",
                table: "battles");

            migrationBuilder.DropColumn(
                name: "missed_b",
                table: "battles");
        }
    }
}
