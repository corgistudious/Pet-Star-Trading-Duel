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
    public DbSet<Guild> Guilds => Set<Guild>();
    public DbSet<GuildMember> GuildMembers => Set<GuildMember>();
    public DbSet<GuildJoinRequest> GuildJoinRequests => Set<GuildJoinRequest>();
    public DbSet<GuildAnnouncement> GuildAnnouncements => Set<GuildAnnouncement>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<PvpChallenge> PvpChallenges => Set<PvpChallenge>();
    public DbSet<PvpQueueEntry> PvpQueue => Set<PvpQueueEntry>();
    public DbSet<PvpPendingAction> PvpPending => Set<PvpPendingAction>();
    public DbSet<Giftcode> Giftcodes => Set<Giftcode>();
    public DbSet<GiftcodeRedemption> GiftcodeRedemptions => Set<GiftcodeRedemption>();
    public DbSet<GameEvent> Events => Set<GameEvent>();
    public DbSet<EventClaim> EventClaims => Set<EventClaim>();
    public DbSet<LoginRewardClaim> LoginRewards => Set<LoginRewardClaim>();
    public DbSet<CardSerialRegistry> SerialRegistry => Set<CardSerialRegistry>();
    public DbSet<CardPassport> Passports => Set<CardPassport>();
    public DbSet<CardProgressEvent> CardProgress => Set<CardProgressEvent>();
    public DbSet<CardValuationSnapshot> ValuationSnapshots => Set<CardValuationSnapshot>();
    public DbSet<MarketListing> Listings => Set<MarketListing>();
    public DbSet<MarketTransaction> MarketTransactions => Set<MarketTransaction>();
    public DbSet<MarketPriceHistory> PriceHistory => Set<MarketPriceHistory>();
    public DbSet<AuctionListing> Auctions => Set<AuctionListing>();
    public DbSet<AuctionBid> AuctionBids => Set<AuctionBid>();
    public DbSet<AuctionSettlement> AuctionSettlements => Set<AuctionSettlement>();
    public DbSet<ReservedBalance> ReservedBalances => Set<ReservedBalance>();
    public DbSet<EconomySystemLedger> SystemLedger => Set<EconomySystemLedger>();
    public DbSet<EconomyAuditEvent> EconomyAudit => Set<EconomyAuditEvent>();
    public DbSet<WishlistEntry> Wishlists => Set<WishlistEntry>();
    public DbSet<PriceAlert> PriceAlerts => Set<PriceAlert>();
    public DbSet<UserFollow> Follows => Set<UserFollow>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<UserBlock> Blocks => Set<UserBlock>();
    public DbSet<UserMute> Mutes => Set<UserMute>();
    public DbSet<UserReport> Reports => Set<UserReport>();
    public DbSet<FeedPost> Posts => Set<FeedPost>();
    public DbSet<FeedPostCard> PostCards => Set<FeedPostCard>();
    public DbSet<FeedLike> Likes => Set<FeedLike>();
    public DbSet<FeedSave> Saves => Set<FeedSave>();
    public DbSet<FeedComment> Comments => Set<FeedComment>();
    public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();
    public DbSet<AccountRestriction> Restrictions => Set<AccountRestriction>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<MailboxMessage> Mailbox => Set<MailboxMessage>();
    public DbSet<AchievementProgress> Achievements => Set<AchievementProgress>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionItem> CollectionItems => Set<CollectionItem>();
    public DbSet<RankedSeason> Seasons => Set<RankedSeason>();
    public DbSet<RankedRecord> RankedRecords => Set<RankedRecord>();

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
        ConfigureExpansion(b);
        ConfigureLive(b);
    }

    private static void MembershipFk<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e,
        System.Linq.Expressions.Expression<Func<T, object?>> fk) where T : class =>
        e.HasOne<ServerMembership>().WithMany().HasForeignKey(fk).HasPrincipalKey(m => new { m.Id, m.ServerId }).OnDelete(DeleteBehavior.Restrict);

    private static void ConfigureExpansion(ModelBuilder b)
    {
        b.Entity<CardInstance>(e =>
        {
            e.HasIndex(x => x.Serial).IsUnique();
            e.Property(x => x.Serial).HasMaxLength(12).IsFixedLength();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_card_serial", "serial ~ '^[A-Z0-9]{12}$'");
                t.HasCheckConstraint("ck_card_skill_level", "skill_level BETWEEN 1 AND 5");
            });
        });
        b.Entity<CardSerialRegistry>(e =>
        {
            e.ToTable("card_serial_registry", t => t.HasCheckConstraint("ck_registry_serial", "serial ~ '^[A-Z0-9]{12}$'"));
            e.HasKey(x => x.Serial);
            e.Property(x => x.Serial).HasMaxLength(12).IsFixedLength();
            e.HasIndex(x => x.CardId).IsUnique();
        });
        b.Entity<CardPassport>(e =>
        {
            e.ToTable("card_passports");
            e.HasKey(x => x.CardId);
            e.HasIndex(x => x.Serial).IsUnique();
            e.HasOne<CardInstance>().WithOne().HasForeignKey<CardPassport>(x => x.CardId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CardProgressEvent>(e => { e.ToTable("card_progress_events"); e.Property(x => x.DataJson).HasColumnType("jsonb"); e.HasIndex(x => x.CardId); });
        b.Entity<CardValuationSnapshot>(e =>
        {
            e.ToTable("card_valuation_snapshots");
            e.HasIndex(x => new { x.ServerId, x.FormId, x.Rarity, x.Currency, x.Day }).IsUnique();
        });
        b.Entity<MarketListing>(e =>
        {
            e.ToTable("market_listings", t =>
            {
                t.HasCheckConstraint("ck_listing_price", "price > 0");
                t.HasCheckConstraint("ck_listing_currency", "currency IN ('RAINBOW_STAR','MINI_GREEN_BEAN')");
            });
            e.HasIndex(x => x.CardId).HasFilter("status = 'active'").IsUnique().HasDatabaseName("ux_listing_one_active_per_card");
            e.HasIndex(x => new { x.ServerId, x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.SellerMembershipId, x.Status });
            MembershipFk(e, x => new { x.SellerMembershipId, x.ServerId });
            e.HasOne<CardInstance>().WithMany().HasForeignKey(x => x.CardId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<MarketTransaction>(e =>
        {
            e.ToTable("market_transactions", t => t.HasCheckConstraint("ck_tx_amounts", "price > 0 AND fee >= 0 AND seller_net = price - fee"));
            e.HasIndex(x => new { x.Kind, x.RefId }).IsUnique();
            e.HasIndex(x => new { x.ServerId, x.FormId, x.Rarity, x.Currency, x.At });
            e.HasIndex(x => x.BuyerMembershipId); e.HasIndex(x => x.SellerMembershipId); e.HasIndex(x => x.CardId);
        });
        b.Entity<MarketPriceHistory>(e =>
        {
            e.ToTable("market_price_history");
            e.HasIndex(x => x.TransactionId).IsUnique();
            e.HasIndex(x => new { x.ServerId, x.FormId, x.Rarity, x.Currency, x.At });
        });
        b.Entity<AuctionListing>(e =>
        {
            e.ToTable("auction_listings", t => t.HasCheckConstraint("ck_auction_prices", "start_price > 0 AND (buyout_price IS NULL OR buyout_price >= start_price)"));
            e.HasIndex(x => x.CardId).HasFilter("status = 'active'").IsUnique().HasDatabaseName("ux_auction_one_active_per_card");
            e.HasIndex(x => new { x.Status, x.EndsAt });
            MembershipFk(e, x => new { x.SellerMembershipId, x.ServerId });
        });
        b.Entity<AuctionBid>(e =>
        {
            e.ToTable("auction_bids", t => t.HasCheckConstraint("ck_bid_amount", "amount > 0"));
            e.HasIndex(x => new { x.AuctionId, x.Amount });
            e.HasOne<AuctionListing>().WithMany().HasForeignKey(x => x.AuctionId);
        });
        b.Entity<AuctionSettlement>(e => { e.ToTable("auction_settlements"); e.HasKey(x => x.AuctionId); });
        b.Entity<ReservedBalance>(e =>
        {
            e.ToTable("reserved_balances", t => t.HasCheckConstraint("ck_reserved_amount", "amount > 0"));
            e.HasIndex(x => new { x.Reason, x.RefId }).IsUnique();
            e.HasIndex(x => new { x.MembershipId, x.Status });
        });
        b.Entity<EconomySystemLedger>(e => { e.ToTable("economy_system_ledger"); e.HasIndex(x => new { x.ServerId, x.Currency, x.Reason, x.RefId }).IsUnique(); });
        b.Entity<EconomyAuditEvent>(e => { e.ToTable("economy_audit_events"); e.Property(x => x.DataJson).HasColumnType("jsonb"); e.HasIndex(x => new { x.ServerId, x.Status, x.At }); });
        b.Entity<WishlistEntry>(e => { e.ToTable("wishlists"); e.HasIndex(x => x.MembershipId); e.HasIndex(x => new { x.ServerId, x.FormId }); MembershipFk(e, x => new { x.MembershipId, x.ServerId }); });
        b.Entity<PriceAlert>(e => { e.ToTable("price_alerts"); e.HasIndex(x => new { x.WishlistId, x.Kind, x.RefId }).IsUnique(); e.HasIndex(x => new { x.MembershipId, x.At }); });
        b.Entity<UserFollow>(e =>
        {
            e.ToTable("user_follows", t => t.HasCheckConstraint("ck_no_self_follow", "follower_membership_id <> followee_membership_id"));
            e.HasKey(x => new { x.FollowerMembershipId, x.FolloweeMembershipId });
            e.HasIndex(x => x.FolloweeMembershipId);
            MembershipFk(e, x => new { x.FollowerMembershipId, x.ServerId });
            e.HasOne<ServerMembership>().WithMany().HasForeignKey(x => new { x.FolloweeMembershipId, x.ServerId }).HasPrincipalKey(m => new { m.Id, m.ServerId }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Friendship>(e =>
        {
            e.ToTable("friendships", t => t.HasCheckConstraint("ck_no_self_friend", "requester_membership_id <> addressee_membership_id"));
            e.HasIndex(x => new { x.RequesterMembershipId, x.AddresseeMembershipId }).IsUnique();
            e.HasIndex(x => x.AddresseeMembershipId);
            MembershipFk(e, x => new { x.RequesterMembershipId, x.ServerId });
            e.HasOne<ServerMembership>().WithMany().HasForeignKey(x => new { x.AddresseeMembershipId, x.ServerId }).HasPrincipalKey(m => new { m.Id, m.ServerId }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<UserBlock>(e => { e.ToTable("user_blocks"); e.HasKey(x => new { x.BlockerMembershipId, x.BlockedMembershipId }); });
        b.Entity<UserMute>(e => { e.ToTable("user_mutes"); e.HasKey(x => new { x.MuterMembershipId, x.MutedMembershipId }); });
        b.Entity<UserReport>(e => { e.ToTable("user_reports"); e.HasIndex(x => new { x.ServerId, x.Status, x.CreatedAt }); e.HasIndex(x => new { x.ReporterMembershipId, x.TargetType, x.TargetId }).IsUnique(); });
        b.Entity<FeedPost>(e =>
        {
            e.ToTable("feed_posts");
            e.Property(x => x.DataJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.ServerId, x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.AuthorMembershipId, x.CreatedAt });
            MembershipFk(e, x => new { x.AuthorMembershipId, x.ServerId });
        });
        b.Entity<FeedPostCard>(e =>
        {
            e.ToTable("feed_post_cards");
            e.HasKey(x => new { x.PostId, x.Position });
            e.Property(x => x.SnapshotJson).HasColumnType("jsonb");
            e.HasOne<FeedPost>().WithMany().HasForeignKey(x => x.PostId);
            e.HasOne<CardInstance>().WithMany().HasForeignKey(x => x.CardId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<FeedLike>(e => { e.ToTable("feed_likes"); e.HasKey(x => new { x.PostId, x.MembershipId }); e.HasOne<FeedPost>().WithMany().HasForeignKey(x => x.PostId); });
        b.Entity<FeedSave>(e => { e.ToTable("feed_saves"); e.HasKey(x => new { x.PostId, x.MembershipId }); e.HasOne<FeedPost>().WithMany().HasForeignKey(x => x.PostId); });
        b.Entity<FeedComment>(e => { e.ToTable("feed_comments"); e.HasIndex(x => new { x.PostId, x.CreatedAt }); e.HasOne<FeedPost>().WithMany().HasForeignKey(x => x.PostId); });
        b.Entity<ModerationAction>(e => { e.ToTable("moderation_actions"); e.HasIndex(x => x.At); });
        b.Entity<AccountRestriction>(e => { e.ToTable("account_restrictions"); e.HasIndex(x => new { x.AccountId, x.Until }); });
        b.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.Property(x => x.DataJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.MembershipId, x.DedupeKey }).IsUnique();
            e.HasIndex(x => new { x.MembershipId, x.CreatedAt });
        });
        b.Entity<MailboxMessage>(e =>
        {
            e.ToTable("mailbox_messages");
            e.Property(x => x.AttachmentsJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.MembershipId, x.DedupeKey }).IsUnique();
        });
        b.Entity<AchievementProgress>(e => { e.ToTable("achievement_progress"); e.HasKey(x => new { x.MembershipId, x.AchievementId }); });
        b.Entity<Collection>(e =>
        {
            e.ToTable("collections");
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.CollectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.MembershipId, x.Name }).IsUnique();
            MembershipFk(e, x => new { x.MembershipId, x.ServerId });
        });
        b.Entity<CollectionItem>(e =>
        {
            e.ToTable("collection_items");
            e.HasKey(x => new { x.CollectionId, x.CardId });
            e.HasOne<CardInstance>().WithMany().HasForeignKey(x => x.CardId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<RankedSeason>(e => { e.ToTable("ranked_seasons"); e.HasIndex(x => new { x.ServerId, x.Status }); });
        b.Entity<RankedRecord>(e =>
        {
            e.ToTable("ranked_records");
            e.HasKey(x => new { x.SeasonId, x.MembershipId });
            e.HasIndex(x => new { x.SeasonId, x.Rating });
            e.HasOne<RankedSeason>().WithMany().HasForeignKey(x => x.SeasonId);
        });
    }

    private static void ConfigureLive(ModelBuilder b)
    {
        b.Entity<Guild>(e =>
        {
            e.ToTable("guilds", t => t.HasCheckConstraint("ck_guild_policy", "join_policy IN ('open','approval')"));
            e.HasIndex(x => new { x.ServerId, x.NameNormalized }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(24);
            e.Property(x => x.Description).HasMaxLength(300);
            MembershipFk(e, x => new { x.LeaderMembershipId, x.ServerId });
        });
        b.Entity<GuildMember>(e =>
        {
            e.ToTable("guild_members", t => t.HasCheckConstraint("ck_guild_role", "role IN ('leader','officer','member')"));
            e.HasKey(x => new { x.GuildId, x.MembershipId });
            e.HasIndex(x => x.MembershipId).IsUnique();   // one guild per membership
            e.HasOne<Guild>().WithMany().HasForeignKey(x => x.GuildId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ServerMembership>().WithMany().HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<GuildJoinRequest>(e =>
        {
            e.ToTable("guild_join_requests");
            e.HasIndex(x => new { x.GuildId, x.MembershipId }).HasFilter("status = 'pending'").IsUnique();
            e.HasOne<Guild>().WithMany().HasForeignKey(x => x.GuildId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<GuildAnnouncement>(e =>
        {
            e.ToTable("guild_announcements");
            e.HasIndex(x => new { x.GuildId, x.CreatedAt });
            e.HasOne<Guild>().WithMany().HasForeignKey(x => x.GuildId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ChatMessage>(e =>
        {
            e.ToTable("chat_messages");
            e.HasIndex(x => new { x.ServerId, x.Channel, x.Id });
            e.HasIndex(x => new { x.AuthorMembershipId, x.CreatedAt });
            e.Property(x => x.Channel).HasMaxLength(64);
            MembershipFk(e, x => new { x.AuthorMembershipId, x.ServerId });
        });
        b.Entity<PvpChallenge>(e =>
        {
            e.ToTable("pvp_challenges");
            e.HasIndex(x => new { x.OpponentMembershipId, x.Status });
            MembershipFk(e, x => new { x.ChallengerMembershipId, x.ServerId });
        });
        b.Entity<PvpQueueEntry>(e => { e.ToTable("pvp_queue"); e.HasKey(x => x.MembershipId); e.HasIndex(x => new { x.ServerId, x.EnqueuedAt }); });
        b.Entity<PvpPendingAction>(e =>
        {
            e.ToTable("pvp_pending_actions");
            e.HasKey(x => new { x.BattleId, x.Round, x.Side });
            e.Property(x => x.ActionsJson).HasColumnType("jsonb");
            e.HasOne<Battle>().WithMany().HasForeignKey(x => x.BattleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Giftcode>(e =>
        {
            e.ToTable("giftcodes", t => t.HasCheckConstraint("ck_giftcode_code", "code ~ '^[A-Z0-9-]{4,32}$'"));
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.RewardsJson).HasColumnType("jsonb");
        });
        b.Entity<GiftcodeRedemption>(e =>
        {
            e.ToTable("giftcode_redemptions");
            e.HasKey(x => new { x.GiftcodeId, x.AccountId, x.ServerId });   // once per account per server
            e.HasOne<Giftcode>().WithMany().HasForeignKey(x => x.GiftcodeId);
        });
        b.Entity<GameEvent>(e => { e.ToTable("game_events"); e.Property(x => x.RewardsJson).HasColumnType("jsonb"); e.HasIndex(x => new { x.ServerId, x.EndsAt }); });
        b.Entity<EventClaim>(e => { e.ToTable("event_claims"); e.HasKey(x => new { x.EventId, x.MembershipId }); e.HasOne<GameEvent>().WithMany().HasForeignKey(x => x.EventId); });
        b.Entity<LoginRewardClaim>(e => { e.ToTable("login_reward_claims"); e.HasKey(x => new { x.MembershipId, x.Day }); });
        b.Entity<FeedPost>(e => e.HasIndex(x => x.GuildId));
    }
}
