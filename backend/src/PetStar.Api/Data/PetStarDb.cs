using Microsoft.EntityFrameworkCore;

namespace PetStar.Api.Data;

public class PetStarDb(DbContextOptions<PetStarDb> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountRole> AccountRoles => Set<AccountRole>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<EmailToken> EmailTokens => Set<EmailToken>();
    public DbSet<GameServer> Servers => Set<GameServer>();
    public DbSet<ServerMembership> Memberships => Set<ServerMembership>();
    public DbSet<PetFamilyRow> PetFamilies => Set<PetFamilyRow>();
    public DbSet<PetFormRow> PetForms => Set<PetFormRow>();
    public DbSet<PetSkillRow> PetSkills => Set<PetSkillRow>();
    public DbSet<CardInstance> Cards => Set<CardInstance>();
    public DbSet<CardOwnershipHistory> CardOwnership => Set<CardOwnershipHistory>();
    public DbSet<CardEvolutionHistory> CardEvolutions => Set<CardEvolutionHistory>();
    public DbSet<CodexEntry> Codex => Set<CodexEntry>();
    public DbSet<ShowcaseSlot> Showcase => Set<ShowcaseSlot>();
    public DbSet<Deck> Decks => Set<Deck>();
    public DbSet<DeckSlot> DeckSlots => Set<DeckSlot>();
    public DbSet<PackTypeRow> PackTypes => Set<PackTypeRow>();
    public DbSet<PackInventory> PackInventory => Set<PackInventory>();
    public DbSet<PackOpening> PackOpenings => Set<PackOpening>();
    public DbSet<PackOpeningResult> PackOpeningResults => Set<PackOpeningResult>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<LedgerEntry> Ledger => Set<LedgerEntry>();
    public DbSet<IapPurchase> IapPurchases => Set<IapPurchase>();
    public DbSet<InventoryItem> Items => Set<InventoryItem>();
    public DbSet<ItemLedgerEntry> ItemLedger => Set<ItemLedgerEntry>();
    public DbSet<Battle> Battles => Set<Battle>();
    public DbSet<BattleRound> BattleRounds => Set<BattleRound>();
    public DbSet<BattleResult> BattleResults => Set<BattleResult>();
    public DbSet<PveProgress> PveProgress => Set<PveProgress>();
    public DbSet<IdempotencyRecord> Idempotency => Set<IdempotencyRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasSequence<long>("account_uid_seq").StartsAt(100000).IncrementsBy(1);
        b.HasSequence<long>("card_instance_seq").StartsAt(1).IncrementsBy(1);

        b.Entity<Account>(e =>
        {
            e.Property(x => x.Id).HasDefaultValueSql("nextval('account_uid_seq')").ValueGeneratedOnAdd();
            e.HasIndex(x => x.EmailNormalized).IsUnique();
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.EmailNormalized).HasMaxLength(254);
            e.Property(x => x.DisplayName).HasMaxLength(24);
            e.Property(x => x.Language).HasMaxLength(8);
            e.HasMany(x => x.Roles).WithOne().HasForeignKey(r => r.AccountId);
        });
        b.Entity<AccountRole>(e =>
        {
            e.HasKey(x => new { x.AccountId, x.Role });
            e.ToTable(t => t.HasCheckConstraint("ck_account_roles_role", "role IN ('owner','admin','gm','moderator')"));
        });
        b.Entity<Session>(e =>
        {
            e.HasIndex(x => x.RefreshTokenHash).IsUnique();
            e.HasIndex(x => x.AccountId);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId);
        });
        b.Entity<EmailToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId);
        });

        b.Entity<GameServer>(e =>
        {
            e.ToTable("game_servers");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.Code).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_servers_status", "status IN ('coming_soon','online','maintenance','merged')"));
        });
        b.Entity<ServerMembership>(e =>
        {
            e.ToTable("server_memberships");
            e.HasIndex(x => new { x.AccountId, x.ServerId }).IsUnique();
            e.HasIndex(x => new { x.ServerId, x.Nickname }).IsUnique();
            e.HasAlternateKey(x => new { x.Id, x.ServerId });   // target for composite FKs (cross-server guard)
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId);
            e.HasOne<GameServer>().WithMany().HasForeignKey(x => x.ServerId);
            e.Property(x => x.Nickname).HasMaxLength(24);
        });

        b.Entity<PetFamilyRow>(e => e.ToTable("pet_families"));
        b.Entity<PetFormRow>(e =>
        {
            e.ToTable("pet_forms");
            e.Property(x => x.DataJson).HasColumnType("jsonb");
            e.HasOne<PetFamilyRow>().WithMany().HasForeignKey(x => x.FamilyId);
            e.HasIndex(x => new { x.FamilyId, x.Stage }).IsUnique();
        });
        b.Entity<PetSkillRow>(e =>
        {
            e.ToTable("pet_skills");
            e.Property(x => x.DataJson).HasColumnType("jsonb");
        });

        b.Entity<CardInstance>(e =>
        {
            e.ToTable("card_instances", t =>
            {
                t.HasCheckConstraint("ck_card_level", "level BETWEEN 1 AND 50");
                t.HasCheckConstraint("ck_card_rarity", "rarity IN ('N','R','SR','SSR','UR','LR','MR','GR','EX','Secret')");
                t.HasCheckConstraint("ck_card_quality", "quality IN ('D','C','B','A','S','SS','SSS')");
                t.HasCheckConstraint("ck_card_finish", "finish IN ('Normal','Foil','Holographic','Shiny','Prismatic')");
            });
            e.Property(x => x.Id).HasDefaultValueSql("nextval('card_instance_seq')").ValueGeneratedOnAdd();
            // owner must be a membership on the SAME server as the card
            e.HasOne<ServerMembership>().WithMany().HasForeignKey(x => new { x.OwnerMembershipId, x.ServerId })
                .HasPrincipalKey(m => new { m.Id, m.ServerId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PetFormRow>().WithMany().HasForeignKey(x => x.FormId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ServerId, x.OwnerMembershipId });
        });
        b.Entity<CardOwnershipHistory>(e =>
        {
            e.ToTable("card_ownership_history");
            e.HasIndex(x => x.CardId);
            e.HasOne<CardInstance>().WithMany().HasForeignKey(x => x.CardId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CardEvolutionHistory>(e =>
        {
            e.ToTable("card_evolution_history");
            e.Property(x => x.CostJson).HasColumnType("jsonb");
            e.HasIndex(x => x.CardId);
            e.HasOne<CardInstance>().WithMany().HasForeignKey(x => x.CardId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CodexEntry>(e =>
        {
            e.ToTable("codex_entries");
            e.HasKey(x => new { x.MembershipId, x.FormId });
            e.HasOne<ServerMembership>().WithMany().HasForeignKey(x => x.MembershipId);
            e.HasOne<PetFormRow>().WithMany().HasForeignKey(x => x.FormId);
        });
        b.Entity<ShowcaseSlot>(e =>
        {
            e.ToTable("card_showcase", t => t.HasCheckConstraint("ck_showcase_slot", "slot BETWEEN 0 AND 4"));
            e.HasKey(x => new { x.MembershipId, x.Slot });
            e.HasIndex(x => x.CardId).IsUnique();
            e.HasOne<CardInstance>().WithMany().HasForeignKey(x => x.CardId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Deck>(e =>
        {
            e.HasMany(x => x.Slots).WithOne().HasForeignKey(s => s.DeckId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ServerMembership>().WithMany().HasForeignKey(x => new { x.MembershipId, x.ServerId })
                .HasPrincipalKey(m => new { m.Id, m.ServerId });
            e.HasIndex(x => x.MembershipId).HasFilter("is_default").IsUnique().HasDatabaseName("ux_decks_one_default");
            e.Property(x => x.Name).HasMaxLength(32);
        });
        b.Entity<DeckSlot>(e =>
        {
            e.ToTable("deck_slots", t => t.HasCheckConstraint("ck_deck_slot_position", "position BETWEEN 0 AND 4"));
            e.HasKey(x => new { x.DeckId, x.Position });
            e.HasIndex(x => new { x.DeckId, x.CardId }).IsUnique();
            e.HasOne<CardInstance>().WithMany().HasForeignKey(x => x.CardId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PackTypeRow>(e =>
        {
            e.ToTable("pack_types");
            e.Property(x => x.DataJson).HasColumnType("jsonb");
        });
        b.Entity<PackInventory>(e =>
        {
            e.ToTable("pack_inventory", t => t.HasCheckConstraint("ck_pack_inventory_qty", "quantity >= 0"));
            e.HasKey(x => new { x.MembershipId, x.PackTypeId });
            e.HasOne<PackTypeRow>().WithMany().HasForeignKey(x => x.PackTypeId);
            e.HasOne<ServerMembership>().WithMany().HasForeignKey(x => x.MembershipId);
        });
        b.Entity<PackOpening>(e =>
        {
            e.ToTable("pack_openings");
            e.HasIndex(x => new { x.MembershipId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.MembershipId, x.CreatedAt });
            e.HasMany(x => x.Results).WithOne().HasForeignKey(r => r.OpeningId);
            e.HasOne<ServerMembership>().WithMany().HasForeignKey(x => new { x.MembershipId, x.ServerId })
                .HasPrincipalKey(m => new { m.Id, m.ServerId });
        });
        b.Entity<PackOpeningResult>(e =>
        {
            e.ToTable("pack_opening_results", t => t.HasCheckConstraint("ck_pack_result_slot", "slot BETWEEN 1 AND 5"));
            e.HasKey(x => new { x.OpeningId, x.Slot });
            e.HasIndex(x => x.CardId).IsUnique();
            e.HasOne<CardInstance>().WithMany().HasForeignKey(x => x.CardId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Wallet>(e =>
        {
            e.ToTable("economy_wallets", t =>
            {
                t.HasCheckConstraint("ck_wallet_non_negative", "balance >= 0");
                t.HasCheckConstraint("ck_wallet_currency", "currency IN ('RAINBOW_STAR','MINI_GREEN_BEAN')");
            });
            e.HasKey(x => new { x.MembershipId, x.Currency });
            e.HasOne<ServerMembership>().WithMany().HasForeignKey(x => x.MembershipId);
        });
        b.Entity<LedgerEntry>(e =>
        {
            e.ToTable("economy_ledger");
            // the same business reference can never credit/debit the same wallet twice
            e.HasIndex(x => new { x.MembershipId, x.Currency, x.Reason, x.RefId }).IsUnique();
            e.HasOne<ServerMembership>().WithMany().HasForeignKey(x => new { x.MembershipId, x.ServerId })
                .HasPrincipalKey(m => new { m.Id, m.ServerId });
        });
        b.Entity<IapPurchase>(e =>
        {
            e.ToTable("iap_purchases");
            e.HasIndex(x => new { x.Platform, x.PurchaseTokenHash }).IsUnique();
            e.Property(x => x.VerificationJson).HasColumnType("jsonb");
            e.HasOne<ServerMembership>().WithMany().HasForeignKey(x => new { x.MembershipId, x.ServerId })
                .HasPrincipalKey(m => new { m.Id, m.ServerId });
        });
        b.Entity<InventoryItem>(e =>
        {
            e.ToTable("inventory_items", t => t.HasCheckConstraint("ck_item_qty", "quantity >= 0"));
            e.HasKey(x => new { x.MembershipId, x.ItemId });
            e.HasOne<ServerMembership>().WithMany().HasForeignKey(x => x.MembershipId);
        });
        b.Entity<ItemLedgerEntry>(e =>
        {
            e.ToTable("item_ledger");
            e.HasIndex(x => new { x.MembershipId, x.ItemId, x.Reason, x.RefId }).IsUnique();
        });

        b.Entity<Battle>(e =>
        {
            e.Property(x => x.StateJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.PlayerAMembershipId, x.Status });
            e.HasOne<ServerMembership>().WithMany().HasForeignKey(x => new { x.PlayerAMembershipId, x.ServerId })
                .HasPrincipalKey(m => new { m.Id, m.ServerId });
        });
        b.Entity<BattleRound>(e =>
        {
            e.ToTable("battle_actions");
            e.HasKey(x => new { x.BattleId, x.Round });
            e.Property(x => x.ActionsAJson).HasColumnType("jsonb");
            e.Property(x => x.ActionsBJson).HasColumnType("jsonb");
            e.Property(x => x.EventsJson).HasColumnType("jsonb");
            e.HasOne<Battle>().WithMany().HasForeignKey(x => x.BattleId);
        });
        b.Entity<BattleResult>(e =>
        {
            e.HasKey(x => x.BattleId);
            e.Property(x => x.RewardsJson).HasColumnType("jsonb");
            e.HasOne<Battle>().WithOne().HasForeignKey<BattleResult>(x => x.BattleId);
        });
        b.Entity<PveProgress>(e =>
        {
            e.ToTable("pve_progress");
            e.HasKey(x => new { x.MembershipId, x.StageId });
        });

        b.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("idempotency_records");
            e.HasKey(x => new { x.AccountId, x.Key });
            e.Property(x => x.ResponseJson).HasColumnType("jsonb");
        });
        b.Entity<AuditLog>(e =>
        {
            e.ToTable("admin_audit_logs");
            e.Property(x => x.DataJson).HasColumnType("jsonb");
            e.HasIndex(x => x.At);
            e.HasIndex(x => new { x.ActorAccountId, x.At });
        });
    }
}
