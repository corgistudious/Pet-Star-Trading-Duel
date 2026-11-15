using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PetStar.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CardEconomySocial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "serial",
                table: "card_instances",
                type: "character(12)",
                fixedLength: true,
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "skill_level",
                table: "card_instances",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "account_restrictions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_restrictions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "achievement_progress",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    achievement_id = table.Column<string>(type: "text", nullable: false),
                    unlocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_achievement_progress", x => new { x.membership_id, x.achievement_id });
                });

            migrationBuilder.CreateTable(
                name: "auction_listings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    seller_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    card_id = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    start_price = table.Column<long>(type: "bigint", nullable: false),
                    buyout_price = table.Column<long>(type: "bigint", nullable: true),
                    min_increment_bps = table.Column<int>(type: "integer", nullable: false),
                    fee_bps = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    highest_bid_id = table.Column<long>(type: "bigint", nullable: true),
                    highest_amount = table.Column<long>(type: "bigint", nullable: true),
                    highest_bidder_membership_id = table.Column<long>(type: "bigint", nullable: true),
                    bid_count = table.Column<int>(type: "integer", nullable: false),
                    extensions = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auction_listings", x => x.id);
                    table.CheckConstraint("ck_auction_prices", "start_price > 0 AND (buyout_price IS NULL OR buyout_price >= start_price)");
                    table.ForeignKey(
                        name: "fk_auction_listings_server_memberships_seller_membership_id_se",
                        columns: x => new { x.seller_membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "auction_settlements",
                columns: table => new
                {
                    auction_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    winning_bid_id = table.Column<long>(type: "bigint", nullable: true),
                    transaction_id = table.Column<long>(type: "bigint", nullable: true),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auction_settlements", x => x.auction_id);
                });

            migrationBuilder.CreateTable(
                name: "card_passports",
                columns: table => new
                {
                    card_id = table.Column<long>(type: "bigint", nullable: false),
                    serial = table.Column<string>(type: "text", nullable: false),
                    issued_server_id = table.Column<int>(type: "integer", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    origin_ref = table.Column<string>(type: "text", nullable: true),
                    form_id_at_issue = table.Column<string>(type: "text", nullable: false),
                    rarity = table.Column<string>(type: "text", nullable: false),
                    quality = table.Column<string>(type: "text", nullable: false),
                    finish = table.Column<string>(type: "text", nullable: false),
                    initial_value_beans = table.Column<long>(type: "bigint", nullable: false),
                    initial_value_stars = table.Column<long>(type: "bigint", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_passports", x => x.card_id);
                    table.ForeignKey(
                        name: "fk_card_passports_card_instances_card_id",
                        column: x => x.card_id,
                        principalTable: "card_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "card_progress_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    card_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    data_json = table.Column<string>(type: "jsonb", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_progress_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "card_serial_registry",
                columns: table => new
                {
                    serial = table.Column<string>(type: "character(12)", fixedLength: true, maxLength: 12, nullable: false),
                    card_id = table.Column<long>(type: "bigint", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_serial_registry", x => x.serial);
                    table.CheckConstraint("ck_registry_serial", "serial ~ '^[A-Z0-9]{12}$'");
                });

            migrationBuilder.CreateTable(
                name: "card_valuation_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    form_id = table.Column<string>(type: "text", nullable: false),
                    rarity = table.Column<string>(type: "text", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    initial_reference = table.Column<long>(type: "bigint", nullable: false),
                    estimated_value = table.Column<long>(type: "bigint", nullable: false),
                    sample_size = table.Column<int>(type: "integer", nullable: false),
                    confidence = table.Column<string>(type: "text", nullable: false),
                    supply = table.Column<int>(type: "integer", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_valuation_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "collections",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collections", x => x.id);
                    table.ForeignKey(
                        name: "fk_collections_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "economy_audit_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    severity = table.Column<string>(type: "text", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: true),
                    card_id = table.Column<long>(type: "bigint", nullable: true),
                    ref_id = table.Column<string>(type: "text", nullable: true),
                    data_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_economy_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "economy_system_ledger",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    ref_id = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_economy_system_ledger", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feed_posts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    author_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    caption = table.Column<string>(type: "text", nullable: false),
                    ref_id = table.Column<string>(type: "text", nullable: true),
                    data_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    featured = table.Column<bool>(type: "boolean", nullable: false),
                    like_count = table.Column<int>(type: "integer", nullable: false),
                    comment_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feed_posts", x => x.id);
                    table.ForeignKey(
                        name: "fk_feed_posts_server_memberships_author_membership_id_server_id",
                        columns: x => new { x.author_membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "friendships",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    requester_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    addressee_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_friendships", x => x.id);
                    table.CheckConstraint("ck_no_self_friend", "requester_membership_id <> addressee_membership_id");
                    table.ForeignKey(
                        name: "fk_friendships_server_memberships_addressee_membership_id_serv",
                        columns: x => new { x.addressee_membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_friendships_server_memberships_requester_membership_id_serv",
                        columns: x => new { x.requester_membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mailbox_messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    title_key = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    attachments_json = table.Column<string>(type: "jsonb", nullable: false),
                    dedupe_key = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mailbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "market_listings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    seller_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    card_id = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    price = table.Column<long>(type: "bigint", nullable: false),
                    fee_bps = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    views = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    buyer_membership_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_listings", x => x.id);
                    table.CheckConstraint("ck_listing_currency", "currency IN ('RAINBOW_STAR','MINI_GREEN_BEAN')");
                    table.CheckConstraint("ck_listing_price", "price > 0");
                    table.ForeignKey(
                        name: "fk_market_listings_card_instances_card_id",
                        column: x => x.card_id,
                        principalTable: "card_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_market_listings_server_memberships_seller_membership_id_ser",
                        columns: x => new { x.seller_membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "market_price_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    form_id = table.Column<string>(type: "text", nullable: false),
                    rarity = table.Column<string>(type: "text", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    price = table.Column<long>(type: "bigint", nullable: false),
                    normalized_price = table.Column<long>(type: "bigint", nullable: false),
                    excluded = table.Column<bool>(type: "boolean", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_price_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "market_transactions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    ref_id = table.Column<long>(type: "bigint", nullable: false),
                    card_id = table.Column<long>(type: "bigint", nullable: false),
                    form_id = table.Column<string>(type: "text", nullable: false),
                    rarity = table.Column<string>(type: "text", nullable: false),
                    seller_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    buyer_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    price = table.Column<long>(type: "bigint", nullable: false),
                    fee = table.Column<long>(type: "bigint", nullable: false),
                    seller_net = table.Column<long>(type: "bigint", nullable: false),
                    suspicious = table.Column<bool>(type: "boolean", nullable: false),
                    suspicion_reasons = table.Column<string>(type: "text", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_transactions", x => x.id);
                    table.CheckConstraint("ck_tx_amounts", "price > 0 AND fee >= 0 AND seller_net = price - fee");
                });

            migrationBuilder.CreateTable(
                name: "moderation_actions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    moderator_account_id = table.Column<long>(type: "bigint", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    target_type = table.Column<string>(type: "text", nullable: false),
                    target_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    report_id = table.Column<long>(type: "bigint", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_actions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    data_json = table.Column<string>(type: "jsonb", nullable: false),
                    dedupe_key = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "price_alerts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wishlist_id = table.Column<long>(type: "bigint", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    ref_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_alerts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ranked_seasons",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ranked_seasons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reserved_balances",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    ref_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reserved_balances", x => x.id);
                    table.CheckConstraint("ck_reserved_amount", "amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "user_blocks",
                columns: table => new
                {
                    blocker_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    blocked_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_blocks", x => new { x.blocker_membership_id, x.blocked_membership_id });
                });

            migrationBuilder.CreateTable(
                name: "user_follows",
                columns: table => new
                {
                    follower_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    followee_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_follows", x => new { x.follower_membership_id, x.followee_membership_id });
                    table.CheckConstraint("ck_no_self_follow", "follower_membership_id <> followee_membership_id");
                    table.ForeignKey(
                        name: "fk_user_follows_server_memberships_followee_membership_id_serv",
                        columns: x => new { x.followee_membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_follows_server_memberships_follower_membership_id_serv",
                        columns: x => new { x.follower_membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_mutes",
                columns: table => new
                {
                    muter_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    muted_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_mutes", x => new { x.muter_membership_id, x.muted_membership_id });
                });

            migrationBuilder.CreateTable(
                name: "user_reports",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    reporter_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    target_type = table.Column<string>(type: "text", nullable: false),
                    target_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    details = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    handled_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    handled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_reports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wishlists",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    form_id = table.Column<string>(type: "text", nullable: true),
                    family_id = table.Column<string>(type: "text", nullable: true),
                    min_rarity = table.Column<string>(type: "text", nullable: true),
                    quality = table.Column<string>(type: "text", nullable: true),
                    finish = table.Column<string>(type: "text", nullable: true),
                    currency = table.Column<string>(type: "text", nullable: true),
                    max_price = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wishlists", x => x.id);
                    table.ForeignKey(
                        name: "fk_wishlists_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "auction_bids",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    auction_id = table.Column<long>(type: "bigint", nullable: false),
                    bidder_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    flagged = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auction_bids", x => x.id);
                    table.CheckConstraint("ck_bid_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_auction_bids_auction_listings_auction_id",
                        column: x => x.auction_id,
                        principalTable: "auction_listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "collection_items",
                columns: table => new
                {
                    collection_id = table.Column<long>(type: "bigint", nullable: false),
                    card_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_items", x => new { x.collection_id, x.card_id });
                    table.ForeignKey(
                        name: "fk_collection_items_card_instances_card_id",
                        column: x => x.card_id,
                        principalTable: "card_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_collection_items_collections_collection_id",
                        column: x => x.collection_id,
                        principalTable: "collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feed_comments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    post_id = table.Column<long>(type: "bigint", nullable: false),
                    author_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feed_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_feed_comments_feed_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "feed_posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feed_likes",
                columns: table => new
                {
                    post_id = table.Column<long>(type: "bigint", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feed_likes", x => new { x.post_id, x.membership_id });
                    table.ForeignKey(
                        name: "fk_feed_likes_feed_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "feed_posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feed_post_cards",
                columns: table => new
                {
                    post_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    card_id = table.Column<long>(type: "bigint", nullable: false),
                    serial = table.Column<string>(type: "text", nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    owner_at_post_membership_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feed_post_cards", x => new { x.post_id, x.position });
                    table.ForeignKey(
                        name: "fk_feed_post_cards_card_instances_card_id",
                        column: x => x.card_id,
                        principalTable: "card_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_feed_post_cards_feed_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "feed_posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feed_saves",
                columns: table => new
                {
                    post_id = table.Column<long>(type: "bigint", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feed_saves", x => new { x.post_id, x.membership_id });
                    table.ForeignKey(
                        name: "fk_feed_saves_feed_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "feed_posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ranked_records",
                columns: table => new
                {
                    season_id = table.Column<int>(type: "integer", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    wins = table.Column<int>(type: "integer", nullable: false),
                    losses = table.Column<int>(type: "integer", nullable: false),
                    streak = table.Column<int>(type: "integer", nullable: false),
                    best_streak = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ranked_records", x => new { x.season_id, x.membership_id });
                    table.ForeignKey(
                        name: "fk_ranked_records_ranked_seasons_season_id",
                        column: x => x.season_id,
                        principalTable: "ranked_seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Backfill for cards created before serials existed (dev/staging data only; production starts with this schema).
            migrationBuilder.Sql("UPDATE card_instances SET serial = upper(substr(md5(gen_random_uuid()::text || id::text), 1, 12)) WHERE serial = '' OR serial IS NULL;");
            migrationBuilder.Sql("INSERT INTO card_serial_registry (serial, card_id, issued_at) SELECT serial, id, created_at FROM card_instances ON CONFLICT DO NOTHING;");
            migrationBuilder.Sql("INSERT INTO card_passports (card_id, serial, issued_server_id, origin, origin_ref, form_id_at_issue, rarity, quality, finish, initial_value_beans, initial_value_stars, issued_at) " +
                                 "SELECT id, serial, server_id, origin, origin_ref, form_id, rarity, quality, finish, 0, 0, created_at FROM card_instances ON CONFLICT DO NOTHING;");

            migrationBuilder.CreateIndex(
                name: "ix_card_instances_serial",
                table: "card_instances",
                column: "serial",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_card_serial",
                table: "card_instances",
                sql: "serial ~ '^[A-Z0-9]{12}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_card_skill_level",
                table: "card_instances",
                sql: "skill_level BETWEEN 1 AND 5");

            migrationBuilder.CreateIndex(
                name: "ix_account_restrictions_account_id_until",
                table: "account_restrictions",
                columns: new[] { "account_id", "until" });

            migrationBuilder.CreateIndex(
                name: "ix_auction_bids_auction_id_amount",
                table: "auction_bids",
                columns: new[] { "auction_id", "amount" });

            migrationBuilder.CreateIndex(
                name: "ix_auction_listings_seller_membership_id_server_id",
                table: "auction_listings",
                columns: new[] { "seller_membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_auction_listings_status_ends_at",
                table: "auction_listings",
                columns: new[] { "status", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ux_auction_one_active_per_card",
                table: "auction_listings",
                column: "card_id",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_card_passports_serial",
                table: "card_passports",
                column: "serial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_card_progress_events_card_id",
                table: "card_progress_events",
                column: "card_id");

            migrationBuilder.CreateIndex(
                name: "ix_card_serial_registry_card_id",
                table: "card_serial_registry",
                column: "card_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_card_valuation_snapshots_server_id_form_id_rarity_currency_",
                table: "card_valuation_snapshots",
                columns: new[] { "server_id", "form_id", "rarity", "currency", "day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_collection_items_card_id",
                table: "collection_items",
                column: "card_id");

            migrationBuilder.CreateIndex(
                name: "ix_collections_membership_id_name",
                table: "collections",
                columns: new[] { "membership_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_collections_membership_id_server_id",
                table: "collections",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_economy_audit_events_server_id_status_at",
                table: "economy_audit_events",
                columns: new[] { "server_id", "status", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_economy_system_ledger_server_id_currency_reason_ref_id",
                table: "economy_system_ledger",
                columns: new[] { "server_id", "currency", "reason", "ref_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feed_comments_post_id_created_at",
                table: "feed_comments",
                columns: new[] { "post_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_feed_post_cards_card_id",
                table: "feed_post_cards",
                column: "card_id");

            migrationBuilder.CreateIndex(
                name: "ix_feed_posts_author_membership_id_created_at",
                table: "feed_posts",
                columns: new[] { "author_membership_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_feed_posts_author_membership_id_server_id",
                table: "feed_posts",
                columns: new[] { "author_membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_feed_posts_server_id_status_created_at",
                table: "feed_posts",
                columns: new[] { "server_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_friendships_addressee_membership_id",
                table: "friendships",
                column: "addressee_membership_id");

            migrationBuilder.CreateIndex(
                name: "ix_friendships_addressee_membership_id_server_id",
                table: "friendships",
                columns: new[] { "addressee_membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_friendships_requester_membership_id_addressee_membership_id",
                table: "friendships",
                columns: new[] { "requester_membership_id", "addressee_membership_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_friendships_requester_membership_id_server_id",
                table: "friendships",
                columns: new[] { "requester_membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_mailbox_messages_membership_id_dedupe_key",
                table: "mailbox_messages",
                columns: new[] { "membership_id", "dedupe_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_market_listings_seller_membership_id_server_id",
                table: "market_listings",
                columns: new[] { "seller_membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_market_listings_seller_membership_id_status",
                table: "market_listings",
                columns: new[] { "seller_membership_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_market_listings_server_id_status_created_at",
                table: "market_listings",
                columns: new[] { "server_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_listing_one_active_per_card",
                table: "market_listings",
                column: "card_id",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_market_price_history_server_id_form_id_rarity_currency_at",
                table: "market_price_history",
                columns: new[] { "server_id", "form_id", "rarity", "currency", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_market_price_history_transaction_id",
                table: "market_price_history",
                column: "transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_market_transactions_buyer_membership_id",
                table: "market_transactions",
                column: "buyer_membership_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_transactions_card_id",
                table: "market_transactions",
                column: "card_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_transactions_kind_ref_id",
                table: "market_transactions",
                columns: new[] { "kind", "ref_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_market_transactions_seller_membership_id",
                table: "market_transactions",
                column: "seller_membership_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_transactions_server_id_form_id_rarity_currency_at",
                table: "market_transactions",
                columns: new[] { "server_id", "form_id", "rarity", "currency", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_actions_at",
                table: "moderation_actions",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_membership_id_created_at",
                table: "notifications",
                columns: new[] { "membership_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_membership_id_dedupe_key",
                table: "notifications",
                columns: new[] { "membership_id", "dedupe_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_alerts_membership_id_at",
                table: "price_alerts",
                columns: new[] { "membership_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_price_alerts_wishlist_id_kind_ref_id",
                table: "price_alerts",
                columns: new[] { "wishlist_id", "kind", "ref_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ranked_records_season_id_rating",
                table: "ranked_records",
                columns: new[] { "season_id", "rating" });

            migrationBuilder.CreateIndex(
                name: "ix_ranked_seasons_server_id_status",
                table: "ranked_seasons",
                columns: new[] { "server_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_reserved_balances_membership_id_status",
                table: "reserved_balances",
                columns: new[] { "membership_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_reserved_balances_reason_ref_id",
                table: "reserved_balances",
                columns: new[] { "reason", "ref_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_follows_followee_membership_id",
                table: "user_follows",
                column: "followee_membership_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_follows_followee_membership_id_server_id",
                table: "user_follows",
                columns: new[] { "followee_membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_follows_follower_membership_id_server_id",
                table: "user_follows",
                columns: new[] { "follower_membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_reports_reporter_membership_id_target_type_target_id",
                table: "user_reports",
                columns: new[] { "reporter_membership_id", "target_type", "target_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_reports_server_id_status_created_at",
                table: "user_reports",
                columns: new[] { "server_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_wishlists_membership_id",
                table: "wishlists",
                column: "membership_id");

            migrationBuilder.CreateIndex(
                name: "ix_wishlists_membership_id_server_id",
                table: "wishlists",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_wishlists_server_id_form_id",
                table: "wishlists",
                columns: new[] { "server_id", "form_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_restrictions");

            migrationBuilder.DropTable(
                name: "achievement_progress");

            migrationBuilder.DropTable(
                name: "auction_bids");

            migrationBuilder.DropTable(
                name: "auction_settlements");

            migrationBuilder.DropTable(
                name: "card_passports");

            migrationBuilder.DropTable(
                name: "card_progress_events");

            migrationBuilder.DropTable(
                name: "card_serial_registry");

            migrationBuilder.DropTable(
                name: "card_valuation_snapshots");

            migrationBuilder.DropTable(
                name: "collection_items");

            migrationBuilder.DropTable(
                name: "economy_audit_events");

            migrationBuilder.DropTable(
                name: "economy_system_ledger");

            migrationBuilder.DropTable(
                name: "feed_comments");

            migrationBuilder.DropTable(
                name: "feed_likes");

            migrationBuilder.DropTable(
                name: "feed_post_cards");

            migrationBuilder.DropTable(
                name: "feed_saves");

            migrationBuilder.DropTable(
                name: "friendships");

            migrationBuilder.DropTable(
                name: "mailbox_messages");

            migrationBuilder.DropTable(
                name: "market_listings");

            migrationBuilder.DropTable(
                name: "market_price_history");

            migrationBuilder.DropTable(
                name: "market_transactions");

            migrationBuilder.DropTable(
                name: "moderation_actions");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "price_alerts");

            migrationBuilder.DropTable(
                name: "ranked_records");

            migrationBuilder.DropTable(
                name: "reserved_balances");

            migrationBuilder.DropTable(
                name: "user_blocks");

            migrationBuilder.DropTable(
                name: "user_follows");

            migrationBuilder.DropTable(
                name: "user_mutes");

            migrationBuilder.DropTable(
                name: "user_reports");

            migrationBuilder.DropTable(
                name: "wishlists");

            migrationBuilder.DropTable(
                name: "auction_listings");

            migrationBuilder.DropTable(
                name: "collections");

            migrationBuilder.DropTable(
                name: "feed_posts");

            migrationBuilder.DropTable(
                name: "ranked_seasons");

            migrationBuilder.DropIndex(
                name: "ix_card_instances_serial",
                table: "card_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_card_serial",
                table: "card_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_card_skill_level",
                table: "card_instances");

            migrationBuilder.DropColumn(
                name: "serial",
                table: "card_instances");

            migrationBuilder.DropColumn(
                name: "skill_level",
                table: "card_instances");
        }
    }
}
