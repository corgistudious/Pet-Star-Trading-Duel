using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PetStar.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CardSystemExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_card_level",
                table: "card_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_card_rarity",
                table: "card_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_card_skill_level",
                table: "card_instances");

            migrationBuilder.AddColumn<bool>(
                name: "awakened",
                table: "card_instances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "breakthrough",
                table: "card_instances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "enhance_level",
                table: "card_instances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "stars",
                table: "card_instances",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "card_engravings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    card_id = table.Column<long>(type: "bigint", nullable: true),
                    slot = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_engravings", x => x.id);
                    table.CheckConstraint("ck_engraving_level", "level BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_engraving_slot", "(card_id IS NULL AND slot IS NULL) OR (card_id IS NOT NULL AND slot BETWEEN 1 AND 3)");
                    table.CheckConstraint("ck_engraving_type", "type IN ('atk','def','hp','spd','crit')");
                    table.ForeignKey(
                        name: "fk_card_engravings_card_instances_card_id",
                        column: x => x.card_id,
                        principalTable: "card_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_card_engravings_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "limited_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    pack_type_id = table.Column<string>(type: "text", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: true),
                    name_vi = table.Column<string>(type: "text", nullable: false),
                    name_en = table.Column<string>(type: "text", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_limit = table.Column<int>(type: "integer", nullable: true),
                    per_player_limit = table.Column<int>(type: "integer", nullable: true),
                    sold = table.Column<int>(type: "integer", nullable: false),
                    cancelled = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_account_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_limited_events", x => x.id);
                    table.CheckConstraint("ck_event_limits", "sold >= 0 AND (total_limit IS NULL OR (total_limit > 0 AND sold <= total_limit)) AND (per_player_limit IS NULL OR per_player_limit > 0)");
                    table.CheckConstraint("ck_event_window", "ends_at > starts_at");
                });

            migrationBuilder.CreateTable(
                name: "membership_vip",
                columns: table => new
                {
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    points = table.Column<long>(type: "bigint", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_membership_vip", x => x.membership_id);
                    table.CheckConstraint("ck_vip", "points >= 0 AND level BETWEEN 0 AND 10");
                    table.ForeignKey(
                        name: "fk_membership_vip_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pack_purchases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    pack_type_id = table.Column<string>(type: "text", nullable: false),
                    pack_version = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<long>(type: "bigint", nullable: true),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pack_purchases", x => x.id);
                    table.CheckConstraint("ck_pack_purchase", "quantity > 0 AND amount >= 0");
                    table.ForeignKey(
                        name: "fk_pack_purchases_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pack_type_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    pack_type_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    data_json = table.Column<string>(type: "jsonb", nullable: false),
                    changed_by_account_id = table.Column<long>(type: "bigint", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pack_type_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "star_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    server_id = table.Column<int>(type: "integer", nullable: false),
                    membership_id = table.Column<long>(type: "bigint", nullable: false),
                    issued_by_account_id = table.Column<long>(type: "bigint", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    max_packs = table.Column<int>(type: "integer", nullable: false),
                    used_packs = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    revoked_by_account_id = table.Column<long>(type: "bigint", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoke_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_star_invitations", x => x.id);
                    table.CheckConstraint("ck_invite_status", "status IN ('active','revoked')");
                    table.CheckConstraint("ck_invite_uses", "max_packs > 0 AND used_packs >= 0 AND used_packs <= max_packs");
                    table.ForeignKey(
                        name: "fk_star_invitations_server_memberships_membership_id_server_id",
                        columns: x => new { x.membership_id, x.server_id },
                        principalTable: "server_memberships",
                        principalColumns: new[] { "id", "server_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_card_breakthrough",
                table: "card_instances",
                sql: "breakthrough BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_card_enhance",
                table: "card_instances",
                sql: "enhance_level BETWEEN 0 AND 20");

            migrationBuilder.AddCheckConstraint(
                name: "ck_card_level",
                table: "card_instances",
                sql: "level BETWEEN 1 AND 70");

            migrationBuilder.AddCheckConstraint(
                name: "ck_card_rarity",
                table: "card_instances",
                sql: "rarity IN ('N','R','SR','SSR','UR','LR','MR','GR','EX','Secret','DV')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_card_skill_level",
                table: "card_instances",
                sql: "skill_level BETWEEN 1 AND 10");

            migrationBuilder.AddCheckConstraint(
                name: "ck_card_stars",
                table: "card_instances",
                sql: "stars BETWEEN 1 AND 10");

            migrationBuilder.CreateIndex(
                name: "ix_card_engravings_card_id_slot",
                table: "card_engravings",
                columns: new[] { "card_id", "slot" },
                unique: true,
                filter: "card_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_card_engravings_membership_id_card_id",
                table: "card_engravings",
                columns: new[] { "membership_id", "card_id" });

            migrationBuilder.CreateIndex(
                name: "ix_card_engravings_membership_id_server_id",
                table: "card_engravings",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_limited_events_pack_type_id_starts_at_ends_at",
                table: "limited_events",
                columns: new[] { "pack_type_id", "starts_at", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_membership_vip_membership_id_server_id",
                table: "membership_vip",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pack_purchases_event_id_membership_id",
                table: "pack_purchases",
                columns: new[] { "event_id", "membership_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pack_purchases_membership_id_pack_type_id_created_at",
                table: "pack_purchases",
                columns: new[] { "membership_id", "pack_type_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pack_purchases_membership_id_server_id",
                table: "pack_purchases",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pack_purchases_server_id_created_at",
                table: "pack_purchases",
                columns: new[] { "server_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pack_type_versions_pack_type_id_version",
                table: "pack_type_versions",
                columns: new[] { "pack_type_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_star_invitations_membership_id_server_id",
                table: "star_invitations",
                columns: new[] { "membership_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "ix_star_invitations_membership_id_status",
                table: "star_invitations",
                columns: new[] { "membership_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_engravings");

            migrationBuilder.DropTable(
                name: "limited_events");

            migrationBuilder.DropTable(
                name: "membership_vip");

            migrationBuilder.DropTable(
                name: "pack_purchases");

            migrationBuilder.DropTable(
                name: "pack_type_versions");

            migrationBuilder.DropTable(
                name: "star_invitations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_card_breakthrough",
                table: "card_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_card_enhance",
                table: "card_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_card_level",
                table: "card_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_card_rarity",
                table: "card_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_card_skill_level",
                table: "card_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_card_stars",
                table: "card_instances");

            migrationBuilder.DropColumn(
                name: "awakened",
                table: "card_instances");

            migrationBuilder.DropColumn(
                name: "breakthrough",
                table: "card_instances");

            migrationBuilder.DropColumn(
                name: "enhance_level",
                table: "card_instances");

            migrationBuilder.DropColumn(
                name: "stars",
                table: "card_instances");

            migrationBuilder.AddCheckConstraint(
                name: "ck_card_level",
                table: "card_instances",
                sql: "level BETWEEN 1 AND 50");

            migrationBuilder.AddCheckConstraint(
                name: "ck_card_rarity",
                table: "card_instances",
                sql: "rarity IN ('N','R','SR','SSR','UR','LR','MR','GR','EX','Secret')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_card_skill_level",
                table: "card_instances",
                sql: "skill_level BETWEEN 1 AND 5");
        }
    }
}
