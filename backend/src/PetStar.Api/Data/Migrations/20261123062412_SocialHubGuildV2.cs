using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PetStar.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SocialHubGuildV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_guild_role",
                table: "guild_members");

            migrationBuilder.AddColumn<string>(
                name: "bubble_id",
                table: "trainer_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "effect_id",
                table: "trainer_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "frame_id",
                table: "trainer_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "title_id",
                table: "trainer_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "banner_id",
                table: "guilds",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "emblem_id",
                table: "guilds",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "exp",
                table: "guilds",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "level",
                table: "guilds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "min_level",
                table: "guilds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "tag",
                table: "guilds",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "visibility",
                table: "guilds",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "edited_at",
                table: "feed_posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_rarity",
                table: "feed_posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "pinned",
                table: "feed_posts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "share_count",
                table: "feed_posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "shared_from_post_id",
                table: "feed_posts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "showcase_kind",
                table: "feed_posts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "visibility",
                table: "feed_posts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "like_count",
                table: "feed_comments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "parent_id",
                table: "feed_comments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "chat_read_markers",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_read_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_read_markers", x => new { x.membership_id, x.channel });
                });

            migrationBuilder.CreateTable(
                name: "cosmetics_owned",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    acquired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cosmetics_owned", x => new { x.membership_id, x.item_id });
                    table.ForeignKey(
                        name: "fk_cosmetics_owned_server_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "server_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "feed_comment_likes",
                columns: table => new
                {
                    comment_id = table.Column<long>(type: "bigint", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feed_comment_likes", x => new { x.comment_id, x.membership_id });
                    table.ForeignKey(
                        name: "fk_feed_comment_likes_feed_comments_comment_id",
                        column: x => x.comment_id,
                        principalTable: "feed_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guild_bosses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    period_key = table.Column<string>(type: "text", nullable: false),
                    boss_id = table.Column<string>(type: "text", nullable: false),
                    max_hp = table.Column<long>(type: "bigint", nullable: false),
                    hp = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    defeated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_bosses", x => x.id);
                    table.CheckConstraint("ck_guild_boss_hp", "hp >= 0 AND hp <= max_hp");
                });

            migrationBuilder.CreateTable(
                name: "guild_contributions",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    period_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    points = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_contributions", x => new { x.guild_id, x.membership_id, x.period_key });
                });

            migrationBuilder.CreateTable(
                name: "guild_invites",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    inviter_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_invites", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_invites_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guild_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    actor_membership_id = table.Column<long>(type: "bigint", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    data_json = table.Column<string>(type: "jsonb", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guild_mission_claims",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    mission_id = table.Column<string>(type: "text", nullable: false),
                    period_key = table.Column<string>(type: "text", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_mission_claims", x => new { x.guild_id, x.mission_id, x.period_key, x.membership_id });
                });

            migrationBuilder.CreateTable(
                name: "guild_mission_progress",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    mission_id = table.Column<string>(type: "text", nullable: false),
                    period_key = table.Column<string>(type: "text", nullable: false),
                    progress = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_mission_progress", x => new { x.guild_id, x.mission_id, x.period_key });
                });

            migrationBuilder.CreateTable(
                name: "guild_war_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    period_key = table.Column<string>(type: "text", nullable: false),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    opponent_guild_id = table.Column<long>(type: "bigint", nullable: true),
                    points = table.Column<int>(type: "integer", nullable: false),
                    wins = table.Column<int>(type: "integer", nullable: false),
                    battles = table.Column<int>(type: "integer", nullable: false),
                    result = table.Column<string>(type: "text", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_war_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leaderboard_seasons",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leaderboard_seasons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leaderboard_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    season_id = table.Column<int>(type: "integer", nullable: false),
                    board = table.Column<string>(type: "text", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    subject_type = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    score = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leaderboard_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "minigame_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    game = table.Column<string>(type: "text", nullable: false),
                    seed = table.Column<long>(type: "bigint", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    score = table.Column<int>(type: "integer", nullable: true),
                    rewarded = table.Column<bool>(type: "boolean", nullable: false),
                    reward_json = table.Column<string>(type: "jsonb", nullable: false),
                    rejected = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_minigame_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "moderation_queue",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    target_type = table.Column<string>(type: "text", nullable: false),
                    target_id = table.Column<long>(type: "bigint", nullable: false),
                    author_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    flags = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_account_id = table.Column<long>(type: "bigint", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_queue", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ranking_rewards",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    season_id = table.Column<int>(type: "integer", nullable: false),
                    board = table.Column<string>(type: "text", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    reward_json = table.Column<string>(type: "jsonb", nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ranking_rewards", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shop_purchases",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<string>(type: "text", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    price = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_purchases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "social_settings",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    default_visibility = table.Column<string>(type: "text", nullable: false),
                    profile_visibility = table.Column<string>(type: "text", nullable: false),
                    default_feed_tab = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_social_settings", x => x.membership_id);
                    table.ForeignKey(
                        name: "fk_social_settings_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trade_offers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    from_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    to_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    offer_card_ids = table.Column<string>(type: "jsonb", nullable: false),
                    request_card_ids = table.Column<string>(type: "jsonb", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trade_offers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guild_boss_attacks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    boss_id = table.Column<long>(type: "bigint", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    damage = table.Column<long>(type: "bigint", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    seed = table.Column<long>(type: "bigint", nullable: false),
                    rounds = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_boss_attacks", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_boss_attacks_guild_bosses_boss_id",
                        column: x => x.boss_id,
                        principalTable: "guild_bosses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guilds_server_id_tag",
                table: "guilds",
                columns: new[] { "server_id", "tag" },
                unique: true,
                filter: "tag <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "ck_guild_role",
                table: "guild_members",
                sql: "role IN ('leader','vice','officer','elite','member')");

            migrationBuilder.CreateIndex(
                name: "ix_feed_posts_server_id_visibility_status_id",
                table: "feed_posts",
                columns: new[] { "server_id", "visibility", "status", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_feed_posts_shared_from_post_id",
                table: "feed_posts",
                column: "shared_from_post_id");

            migrationBuilder.CreateIndex(
                name: "ix_feed_comments_parent_id",
                table: "feed_comments",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_boss_attacks_boss_id_membership_id_day",
                table: "guild_boss_attacks",
                columns: new[] { "boss_id", "membership_id", "day" });

            migrationBuilder.CreateIndex(
                name: "ix_guild_bosses_guild_id_period_key",
                table: "guild_bosses",
                columns: new[] { "guild_id", "period_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guild_contributions_period_key_points",
                table: "guild_contributions",
                columns: new[] { "period_key", "points" });

            migrationBuilder.CreateIndex(
                name: "ix_guild_invites_guild_id_membership_id",
                table: "guild_invites",
                columns: new[] { "guild_id", "membership_id" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_guild_logs_guild_id_id",
                table: "guild_logs",
                columns: new[] { "guild_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_guild_war_entries_period_key_guild_id",
                table: "guild_war_entries",
                columns: new[] { "period_key", "guild_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leaderboard_seasons_server_id_key",
                table: "leaderboard_seasons",
                columns: new[] { "server_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leaderboard_snapshots_season_id_board_rank",
                table: "leaderboard_snapshots",
                columns: new[] { "season_id", "board", "rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_minigame_sessions_game_score",
                table: "minigame_sessions",
                columns: new[] { "game", "score" });

            migrationBuilder.CreateIndex(
                name: "ix_minigame_sessions_membership_id_day",
                table: "minigame_sessions",
                columns: new[] { "membership_id", "day" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_queue_server_id_status_created_at",
                table: "moderation_queue",
                columns: new[] { "server_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_queue_target_type_target_id",
                table: "moderation_queue",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ranking_rewards_season_id_board_membership_id",
                table: "ranking_rewards",
                columns: new[] { "season_id", "board", "membership_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_purchases_membership_id_at",
                table: "shop_purchases",
                columns: new[] { "membership_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_shop_purchases_membership_id_idempotency_key",
                table: "shop_purchases",
                columns: new[] { "membership_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_social_settings_membership_id_server_id",
                table: "social_settings",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_trade_offers_from_membership_id_status",
                table: "trade_offers",
                columns: new[] { "from_membership_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_trade_offers_to_membership_id_status",
                table: "trade_offers",
                columns: new[] { "to_membership_id", "status" });

            // existing rows get the real defaults (EF adds non-null columns with empty/zero defaults)
            migrationBuilder.Sql("UPDATE trainer_profiles SET frame_id = 'frame_default' WHERE frame_id = '';");
            migrationBuilder.Sql("UPDATE trainer_profiles SET bubble_id = 'bubble_default' WHERE bubble_id = '';");
            migrationBuilder.Sql("UPDATE guilds SET emblem_id = 'emblem_star' WHERE emblem_id = '';");
            migrationBuilder.Sql("UPDATE guilds SET banner_id = 'banner_sky' WHERE banner_id = '';");
            migrationBuilder.Sql("UPDATE guilds SET visibility = 'public' WHERE visibility = '';");
            migrationBuilder.Sql("UPDATE guilds SET level = 1 WHERE level = 0;");
            migrationBuilder.Sql("UPDATE guilds SET min_level = 1 WHERE min_level = 0;");
            // guild-only posts stay guild-only; everything else keeps its previous public visibility
            migrationBuilder.Sql("UPDATE feed_posts SET visibility = CASE WHEN guild_id IS NOT NULL THEN 'guild' ELSE 'public' END WHERE visibility = '';");
            migrationBuilder.Sql(@"UPDATE feed_posts p SET max_rarity = sub.r FROM (
                SELECT pc.post_id, max(CASE c.rarity WHEN 'N' THEN 0 WHEN 'R' THEN 1 WHEN 'SR' THEN 2 WHEN 'SSR' THEN 3 WHEN 'UR' THEN 4 WHEN 'LR' THEN 5 WHEN 'MR' THEN 6 WHEN 'GR' THEN 7 WHEN 'EX' THEN 8 WHEN 'Secret' THEN 9 WHEN 'DV' THEN 10 ELSE 0 END) AS r
                FROM feed_post_cards pc JOIN card_instances c ON c.id = pc.card_id GROUP BY pc.post_id) sub WHERE sub.post_id = p.id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_read_markers");

            migrationBuilder.DropTable(
                name: "cosmetics_owned");

            migrationBuilder.DropTable(
                name: "feed_comment_likes");

            migrationBuilder.DropTable(
                name: "guild_boss_attacks");

            migrationBuilder.DropTable(
                name: "guild_contributions");

            migrationBuilder.DropTable(
                name: "guild_invites");

            migrationBuilder.DropTable(
                name: "guild_logs");

            migrationBuilder.DropTable(
                name: "guild_mission_claims");

            migrationBuilder.DropTable(
                name: "guild_mission_progress");

            migrationBuilder.DropTable(
                name: "guild_war_entries");

            migrationBuilder.DropTable(
                name: "leaderboard_seasons");

            migrationBuilder.DropTable(
                name: "leaderboard_snapshots");

            migrationBuilder.DropTable(
                name: "minigame_sessions");

            migrationBuilder.DropTable(
                name: "moderation_queue");

            migrationBuilder.DropTable(
                name: "ranking_rewards");

            migrationBuilder.DropTable(
                name: "shop_purchases");

            migrationBuilder.DropTable(
                name: "social_settings");

            migrationBuilder.DropTable(
                name: "trade_offers");

            migrationBuilder.DropTable(
                name: "guild_bosses");

            migrationBuilder.DropIndex(
                name: "ix_guilds_server_id_tag",
                table: "guilds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_guild_role",
                table: "guild_members");

            migrationBuilder.DropIndex(
                name: "ix_feed_posts_server_id_visibility_status_id",
                table: "feed_posts");

            migrationBuilder.DropIndex(
                name: "ix_feed_posts_shared_from_post_id",
                table: "feed_posts");

            migrationBuilder.DropIndex(
                name: "ix_feed_comments_parent_id",
                table: "feed_comments");

            migrationBuilder.DropColumn(
                name: "bubble_id",
                table: "trainer_profiles");

            migrationBuilder.DropColumn(
                name: "effect_id",
                table: "trainer_profiles");

            migrationBuilder.DropColumn(
                name: "frame_id",
                table: "trainer_profiles");

            migrationBuilder.DropColumn(
                name: "title_id",
                table: "trainer_profiles");

            migrationBuilder.DropColumn(
                name: "banner_id",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "emblem_id",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "exp",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "level",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "min_level",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "tag",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "visibility",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "edited_at",
                table: "feed_posts");

            migrationBuilder.DropColumn(
                name: "max_rarity",
                table: "feed_posts");

            migrationBuilder.DropColumn(
                name: "pinned",
                table: "feed_posts");

            migrationBuilder.DropColumn(
                name: "share_count",
                table: "feed_posts");

            migrationBuilder.DropColumn(
                name: "shared_from_post_id",
                table: "feed_posts");

            migrationBuilder.DropColumn(
                name: "showcase_kind",
                table: "feed_posts");

            migrationBuilder.DropColumn(
                name: "visibility",
                table: "feed_posts");

            migrationBuilder.DropColumn(
                name: "like_count",
                table: "feed_comments");

            migrationBuilder.DropColumn(
                name: "parent_id",
                table: "feed_comments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_guild_role",
                table: "guild_members",
                sql: "role IN ('leader','officer','member')");
        }
    }
}
