using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PetStar.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "account_uid_seq",
                startValue: 100000L);

            migrationBuilder.CreateSequence(
                name: "card_instance_seq");

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('account_uid_seq')"),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    email_normalized = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    avatar_id = table.Column<string>(type: "text", nullable: false),
                    avatar_frame_id = table.Column<string>(type: "text", nullable: false),
                    title_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    ban_reason = table.Column<string>(type: "text", nullable: true),
                    banned_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_login_count = table.Column<int>(type: "integer", nullable: false),
                    lockout_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "admin_audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    actor_account_id = table.Column<long>(type: "bigint", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    target_type = table.Column<string>(type: "text", nullable: true),
                    target_id = table.Column<string>(type: "text", nullable: true),
                    server_id = table.Column<int>(type: "integer", nullable: true),
                    data_json = table.Column<string>(type: "jsonb", nullable: false),
                    ip = table.Column<string>(type: "text", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "game_servers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    registration_open = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merged_into_server_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_servers", x => x.id);
                    table.CheckConstraint("ck_servers_status", "status IN ('coming_soon','online','maintenance','merged')");
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    endpoint = table.Column<string>(type: "text", nullable: false),
                    request_hash = table.Column<string>(type: "text", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_records", x => new { x.account_id, x.key });
                });

            migrationBuilder.CreateTable(
                name: "item_ledger",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<string>(type: "text", nullable: false),
                    delta = table.Column<long>(type: "bigint", nullable: false),
                    balance_after = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    ref_id = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_ledger", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pack_types",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    data_json = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pack_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pet_families",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    element = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    stages = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pet_families", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pet_skills",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    data_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pet_skills", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pve_progress",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    stage_id = table.Column<string>(type: "text", nullable: false),
                    clear_count = table.Column<int>(type: "integer", nullable: false),
                    first_cleared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    best_rounds = table.Column<int>(type: "integer", nullable: false),
                    daily_date = table.Column<DateOnly>(type: "date", nullable: false),
                    daily_rewarded_clears = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pve_progress", x => new { x.membership_id, x.stage_id });
                });

            migrationBuilder.CreateTable(
                name: "account_roles",
                columns: table => new
                {
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_roles", x => new { x.account_id, x.role });
                    table.CheckConstraint("ck_account_roles_role", "role IN ('owner','admin','gm','moderator')");
                    table.ForeignKey(
                        name: "fk_account_roles_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_email_tokens_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    refresh_token_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoke_reason = table.Column<string>(type: "text", nullable: true),
                    platform = table.Column<string>(type: "text", nullable: true),
                    device_name = table.Column<string>(type: "text", nullable: true),
                    ip = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sessions_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "server_memberships",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    nickname = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    exp = table.Column<int>(type: "integer", nullable: false),
                    favorite_card_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    origin_server_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_server_memberships", x => x.id);
                    table.UniqueConstraint("ak_server_memberships_id_server_id", x => new { x.id, x.server_id });
                    table.ForeignKey(
                        name: "fk_server_memberships_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_server_memberships_game_servers_server_id",
                        column: x => x.server_id,
                        principalTable: "game_servers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pet_forms",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    family_id = table.Column<string>(type: "text", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    data_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pet_forms", x => x.id);
                    table.ForeignKey(
                        name: "fk_pet_forms_pet_families_family_id",
                        column: x => x.family_id,
                        principalTable: "pet_families",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "battles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    stage_id = table.Column<string>(type: "text", nullable: true),
                    player_a_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    player_b_membership_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    state_json = table.Column<string>(type: "jsonb", nullable: false),
                    seed = table.Column<long>(type: "bigint", nullable: false),
                    round = table.Column<int>(type: "integer", nullable: false),
                    winner_side = table.Column<int>(type: "integer", nullable: true),
                    end_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    round_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_battles", x => x.id);
                    table.ForeignKey(
                        name: "fk_battles_server_memberships_player_a_membership_id_server_id",
                        columns: x => new { x.player_a_membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "decks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_decks", x => x.id);
                    table.ForeignKey(
                        name: "fk_decks_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "economy_ledger",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    delta = table.Column<long>(type: "bigint", nullable: false),
                    balance_after = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    ref_id = table.Column<string>(type: "text", nullable: false),
                    actor_account_id = table.Column<long>(type: "bigint", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_economy_ledger", x => x.id);
                    table.ForeignKey(
                        name: "fk_economy_ledger_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "economy_wallets",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    balance = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_economy_wallets", x => new { x.membership_id, x.currency });
                    table.CheckConstraint("ck_wallet_currency", "currency IN ('RAINBOW_STAR','MINI_GREEN_BEAN')");
                    table.CheckConstraint("ck_wallet_non_negative", "balance >= 0");
                    table.ForeignKey(
                        name: "fk_economy_wallets_server_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "server_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iap_purchases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: false),
                    product_id = table.Column<string>(type: "text", nullable: false),
                    purchase_token_hash = table.Column<string>(type: "text", nullable: false),
                    order_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    reject_reason = table.Column<string>(type: "text", nullable: true),
                    verification_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iap_purchases", x => x.id);
                    table.ForeignKey(
                        name: "fk_iap_purchases_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_items",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_items", x => new { x.membership_id, x.item_id });
                    table.CheckConstraint("ck_item_qty", "quantity >= 0");
                    table.ForeignKey(
                        name: "fk_inventory_items_server_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "server_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pack_inventory",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    pack_type_id = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pack_inventory", x => new { x.membership_id, x.pack_type_id });
                    table.CheckConstraint("ck_pack_inventory_qty", "quantity >= 0");
                    table.ForeignKey(
                        name: "fk_pack_inventory_pack_types_pack_type_id",
                        column: x => x.pack_type_id,
                        principalTable: "pack_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pack_inventory_server_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "server_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pack_openings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    pack_type_id = table.Column<string>(type: "text", nullable: false),
                    pack_config_version = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pack_openings", x => x.id);
                    table.ForeignKey(
                        name: "fk_pack_openings_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_instances",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('card_instance_seq')"),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    owner_membership_id = table.Column<long>(type: "bigint", nullable: false),
                    form_id = table.Column<string>(type: "text", nullable: false),
                    rarity = table.Column<string>(type: "text", nullable: false),
                    quality = table.Column<string>(type: "text", nullable: false),
                    finish = table.Column<string>(type: "text", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    exp = table.Column<int>(type: "integer", nullable: false),
                    is_favorite = table.Column<bool>(type: "boolean", nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    busy_state = table.Column<string>(type: "text", nullable: true),
                    origin = table.Column<string>(type: "text", nullable: false),
                    origin_ref = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_instances", x => x.id);
                    table.CheckConstraint("ck_card_finish", "finish IN ('Normal','Foil','Holographic','Shiny','Prismatic')");
                    table.CheckConstraint("ck_card_level", "level BETWEEN 1 AND 50");
                    table.CheckConstraint("ck_card_quality", "quality IN ('D','C','B','A','S','SS','SSS')");
                    table.CheckConstraint("ck_card_rarity", "rarity IN ('N','R','SR','SSR','UR','LR','MR','GR','EX','Secret')");
                    table.ForeignKey(
                        name: "fk_card_instances_pet_forms_form_id",
                        column: x => x.form_id,
                        principalTable: "pet_forms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_card_instances_server_memberships_owner_membership_id_serve",
                        columns: x => new { x.owner_membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "codex_entries",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    form_id = table.Column<string>(type: "text", nullable: false),
                    first_card_id = table.Column<long>(type: "bigint", nullable: false),
                    discovered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_codex_entries", x => new { x.membership_id, x.form_id });
                    table.ForeignKey(
                        name: "fk_codex_entries_pet_forms_form_id",
                        column: x => x.form_id,
                        principalTable: "pet_forms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_codex_entries_server_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "server_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "battle_actions",
                columns: table => new
                {
                    battle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round = table.Column<int>(type: "integer", nullable: false),
                    actions_a_json = table.Column<string>(type: "jsonb", nullable: false),
                    actions_b_json = table.Column<string>(type: "jsonb", nullable: false),
                    events_json = table.Column<string>(type: "jsonb", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_battle_actions", x => new { x.battle_id, x.round });
                    table.ForeignKey(
                        name: "fk_battle_actions_battles_battle_id",
                        column: x => x.battle_id,
                        principalTable: "battles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "battle_results",
                columns: table => new
                {
                    battle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    winner_membership_id = table.Column<long>(type: "bigint", nullable: true),
                    rewards_json = table.Column<string>(type: "jsonb", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_battle_results", x => x.battle_id);
                    table.ForeignKey(
                        name: "fk_battle_results_battles_battle_id",
                        column: x => x.battle_id,
                        principalTable: "battles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_evolution_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    card_id = table.Column<long>(type: "bigint", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    from_form_id = table.Column<string>(type: "text", nullable: false),
                    to_form_id = table.Column<string>(type: "text", nullable: false),
                    cost_json = table.Column<string>(type: "jsonb", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_evolution_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_card_evolution_history_card_instances_card_id",
                        column: x => x.card_id,
                        principalTable: "card_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "card_ownership_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    card_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    from_membership_id = table.Column<long>(type: "bigint", nullable: true),
                    to_membership_id = table.Column<long>(type: "bigint", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    ref_id = table.Column<string>(type: "text", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_ownership_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_card_ownership_history_card_instances_card_id",
                        column: x => x.card_id,
                        principalTable: "card_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "card_showcase",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    card_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_showcase", x => new { x.membership_id, x.slot });
                    table.CheckConstraint("ck_showcase_slot", "slot BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "fk_card_showcase_card_instances_card_id",
                        column: x => x.card_id,
                        principalTable: "card_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "deck_slots",
                columns: table => new
                {
                    deck_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    card_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deck_slots", x => new { x.deck_id, x.position });
                    table.CheckConstraint("ck_deck_slot_position", "position BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "fk_deck_slots_card_instances_card_id",
                        column: x => x.card_id,
                        principalTable: "card_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_deck_slots_decks_deck_id",
                        column: x => x.deck_id,
                        principalTable: "decks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pack_opening_results",
                columns: table => new
                {
                    opening_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    card_id = table.Column<long>(type: "bigint", nullable: false),
                    form_id = table.Column<string>(type: "text", nullable: false),
                    rarity = table.Column<string>(type: "text", nullable: false),
                    quality = table.Column<string>(type: "text", nullable: false),
                    finish = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pack_opening_results", x => new { x.opening_id, x.slot });
                    table.CheckConstraint("ck_pack_result_slot", "slot BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_pack_opening_results_card_instances_card_id",
                        column: x => x.card_id,
                        principalTable: "card_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pack_opening_results_pack_openings_opening_id",
                        column: x => x.opening_id,
                        principalTable: "pack_openings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_email_normalized",
                table: "accounts",
                column: "email_normalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_logs_actor_account_id_at",
                table: "admin_audit_logs",
                columns: new[] { "actor_account_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_logs_at",
                table: "admin_audit_logs",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_battles_player_a_membership_id_server_id",
                table: "battles",
                columns: new[] { "player_a_membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_battles_player_a_membership_id_status",
                table: "battles",
                columns: new[] { "player_a_membership_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_card_evolution_history_card_id",
                table: "card_evolution_history",
                column: "card_id");

            migrationBuilder.CreateIndex(
                name: "ix_card_instances_form_id",
                table: "card_instances",
                column: "form_id");

            migrationBuilder.CreateIndex(
                name: "ix_card_instances_owner_membership_id_server_id",
                table: "card_instances",
                columns: new[] { "owner_membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_card_instances_server_id_owner_membership_id",
                table: "card_instances",
                columns: new[] { "server_id", "owner_membership_id" });

            migrationBuilder.CreateIndex(
                name: "ix_card_ownership_history_card_id",
                table: "card_ownership_history",
                column: "card_id");

            migrationBuilder.CreateIndex(
                name: "ix_card_showcase_card_id",
                table: "card_showcase",
                column: "card_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_codex_entries_form_id",
                table: "codex_entries",
                column: "form_id");

            migrationBuilder.CreateIndex(
                name: "ix_deck_slots_card_id",
                table: "deck_slots",
                column: "card_id");

            migrationBuilder.CreateIndex(
                name: "ix_deck_slots_deck_id_card_id",
                table: "deck_slots",
                columns: new[] { "deck_id", "card_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_decks_membership_id_server_id",
                table: "decks",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ux_decks_one_default",
                table: "decks",
                column: "membership_id",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_economy_ledger_membership_id_currency_reason_ref_id",
                table: "economy_ledger",
                columns: new[] { "membership_id", "currency", "reason", "ref_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_economy_ledger_membership_id_server_id",
                table: "economy_ledger",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_email_tokens_account_id",
                table: "email_tokens",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_tokens_token_hash",
                table: "email_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_game_servers_code",
                table: "game_servers",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iap_purchases_membership_id_server_id",
                table: "iap_purchases",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_iap_purchases_platform_purchase_token_hash",
                table: "iap_purchases",
                columns: new[] { "platform", "purchase_token_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_item_ledger_membership_id_item_id_reason_ref_id",
                table: "item_ledger",
                columns: new[] { "membership_id", "item_id", "reason", "ref_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pack_inventory_pack_type_id",
                table: "pack_inventory",
                column: "pack_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_pack_opening_results_card_id",
                table: "pack_opening_results",
                column: "card_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pack_openings_membership_id_created_at",
                table: "pack_openings",
                columns: new[] { "membership_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pack_openings_membership_id_idempotency_key",
                table: "pack_openings",
                columns: new[] { "membership_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pack_openings_membership_id_server_id",
                table: "pack_openings",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pet_forms_family_id_stage",
                table: "pet_forms",
                columns: new[] { "family_id", "stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_server_memberships_account_id_server_id",
                table: "server_memberships",
                columns: new[] { "account_id", "server_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_server_memberships_server_id_nickname",
                table: "server_memberships",
                columns: new[] { "server_id", "nickname" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_account_id",
                table: "sessions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_refresh_token_hash",
                table: "sessions",
                column: "refresh_token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_roles");

            migrationBuilder.DropTable(
                name: "admin_audit_logs");

            migrationBuilder.DropTable(
                name: "battle_actions");

            migrationBuilder.DropTable(
                name: "battle_results");

            migrationBuilder.DropTable(
                name: "card_evolution_history");

            migrationBuilder.DropTable(
                name: "card_ownership_history");

            migrationBuilder.DropTable(
                name: "card_showcase");

            migrationBuilder.DropTable(
                name: "codex_entries");

            migrationBuilder.DropTable(
                name: "deck_slots");

            migrationBuilder.DropTable(
                name: "economy_ledger");

            migrationBuilder.DropTable(
                name: "economy_wallets");

            migrationBuilder.DropTable(
                name: "email_tokens");

            migrationBuilder.DropTable(
                name: "iap_purchases");

            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "inventory_items");

            migrationBuilder.DropTable(
                name: "item_ledger");

            migrationBuilder.DropTable(
                name: "pack_inventory");

            migrationBuilder.DropTable(
                name: "pack_opening_results");

            migrationBuilder.DropTable(
                name: "pet_skills");

            migrationBuilder.DropTable(
                name: "pve_progress");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "battles");

            migrationBuilder.DropTable(
                name: "decks");

            migrationBuilder.DropTable(
                name: "pack_types");

            migrationBuilder.DropTable(
                name: "card_instances");

            migrationBuilder.DropTable(
                name: "pack_openings");

            migrationBuilder.DropTable(
                name: "pet_forms");

            migrationBuilder.DropTable(
                name: "server_memberships");

            migrationBuilder.DropTable(
                name: "pet_families");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "game_servers");

            migrationBuilder.DropSequence(
                name: "account_uid_seq");

            migrationBuilder.DropSequence(
                name: "card_instance_seq");
        }
    }
}
