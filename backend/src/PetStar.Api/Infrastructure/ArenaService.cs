using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Features;
using PetStar.Rules.Arena;

namespace PetStar.Api.Infrastructure;

/// <summary>Arena card definitions (loaded once) and the stored definition version used by every new match.</summary>
public sealed class ArenaRulesProvider(ArenaRules rules)
{
    public ArenaRules Rules { get; } = rules;
}

public sealed record ArenaViewDto(Guid Id, string Mode, string Status, int You, int Version, DateTimeOffset? DeadlineAt, DateTimeOffset ServerNow,
    string? OpponentName, ArenaState State, string? RoomCode, string? RewardsJson);

public sealed class ArenaDeckDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public List<string> EnergyTypes { get; set; } = new();
    public List<ArenaDeckEntry> Cards { get; set; } = new();
}

/// <summary>
/// Server side of the Pet Battle Arena: decks built only from what the player owns, the authoritative engine, AI sides,
/// timers, an append-only action log (with idempotent action ids), snapshots, filtered views, replays and rewards.
/// </summary>
public sealed class ArenaService(PetStarDb db, GameData data, ArenaRulesProvider rp, EconomyService eco, AchievementService ach, RealtimeHub hub)
{
    private static readonly JsonSerializerOptions J = IdempotencyService.Json;
    public ArenaRules Rules => rp.Rules;
    public const int SnapshotEvery = 25;

    // ------------------------------------------------------------------ collection & decks

    /// <summary>Grants the starter trainer cards once (ledger reference makes it idempotent). Needs a transaction.</summary>
    public async Task EnsureStarter(long mid, int sid)
    {
        foreach (var (id, n) in Rules.Starter.OrderBy(x => x.Key, StringComparer.Ordinal))
            await eco.Item(mid, sid, id, n, "starter_arena", "starter_arena", tolerateDuplicate: true);
    }

    public async Task<Dictionary<string, long>> OwnedTrainers(long mid)
    {
        var ids = Rules.Cards.Where(c => c.Kind != "pet").Select(c => c.Id).ToList();
        return await db.Items.AsNoTracking().Where(i => i.MembershipId == mid && ids.Contains(i.ItemId) && i.Quantity > 0).ToDictionaryAsync(i => i.ItemId, i => i.Quantity);
    }

    public static ArenaDeckDto ToDto(ArenaDeckRow d) => new()
    {
        Id = d.Id, Name = d.Name, IsDefault = d.IsDefault,
        EnergyTypes = d.EnergyTypes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
        Cards = d.Cards.OrderBy(c => c.Id).Select(c => new ArenaDeckEntry { DefId = c.DefId, CardInstanceId = c.CardInstanceId ?? 0, Count = c.Count }).ToList(),
    };

    /// <summary>Checks ownership, card locks (listed / auction / review) and the deck rules. Throws readable codes.</summary>
    public async Task<(List<ArenaDeckCard> deck, List<string> energy)> Validate(long mid, int sid, List<ArenaDeckEntry> entries, List<string>? energy)
    {
        var instIds = entries.Where(e => e.CardInstanceId > 0).Select(e => e.CardInstanceId).ToList();
        var cards = await db.Cards.AsNoTracking().Where(c => instIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        foreach (var e in entries.Where(e => Rules.Has(e.DefId) && Rules.Def(e.DefId).Kind == "pet"))
        {
            if (!cards.TryGetValue(e.CardInstanceId, out var c) || c.OwnerMembershipId != mid || c.ServerId != sid) throw ApiException.Conflict("deck_card_not_owned");
            if (c.FormId != e.DefId) throw ApiException.Conflict("deck_card_mismatch");
            if (c.BusyState != null) throw ApiException.Conflict("deck_card_busy");
        }
        var owned = await OwnedTrainers(mid);
        foreach (var g in entries.Where(e => Rules.Has(e.DefId) && Rules.Def(e.DefId).Kind != "pet").GroupBy(e => e.DefId))
            if (owned.GetValueOrDefault(g.Key) < g.Sum(e => e.Count)) throw ApiException.Conflict("deck_trainer_not_owned");
        var types = energy is { Count: > 0 } ? energy : null;
        var err = ArenaDecks.Validate(Rules, entries, types);
        if (err != null) throw ApiException.Conflict(err);
        var deck = ArenaDecks.Expand(entries);
        return (deck, types?.ToList() ?? ArenaDecks.DefaultEnergy(Rules, deck.Select(d => d.DefId)));
    }

    public async Task<(List<ArenaDeckCard> deck, List<string> energy, ArenaDeckRow row)> PlayerDeck(long mid, int sid, long? deckId)
    {
        var row = await db.ArenaDecks.AsNoTracking().Include(d => d.Cards).Where(d => d.MembershipId == mid && (deckId == null ? d.IsDefault : d.Id == deckId)).FirstOrDefaultAsync()
                  ?? throw ApiException.Conflict("no_arena_deck");
        var dto = ToDto(row);
        var (deck, energy) = await Validate(mid, sid, dto.Cards, dto.EnergyTypes);
        return (deck, energy, row);
    }

    /// <summary>A legal deck from the player's free Pet cards (strongest evolution lines) and owned trainers.</summary>
    public async Task<List<ArenaDeckEntry>> AutoDeck(long mid, int sid)
    {
        var cards = await db.Cards.AsNoTracking().Where(c => c.OwnerMembershipId == mid && c.ServerId == sid && c.BusyState == null).Select(c => new { c.Id, c.FormId }).ToListAsync();
        var pets = cards.Where(c => Rules.Has(c.FormId)).Select(c => (c.FormId, c.Id)).ToList();
        if (!pets.Any(p => Rules.Def(p.FormId).IsBasic)) throw ApiException.Conflict("no_basic_pet_owned");
        var trainers = (await OwnedTrainers(mid)).ToDictionary(kv => kv.Key, kv => (int)Math.Min(kv.Value, Rules.Config.MaxCopies));
        var deck = ArenaDecks.Auto(Rules, pets, trainers);
        if (deck.Sum(e => e.Count) != Rules.Config.DeckSize) throw ApiException.Conflict("not_enough_cards");
        return deck;
    }

    /// <summary>AI deck for a PvE stage: the stage's Pet families (all forms) plus a full trainer set. AI cards are not owned items.</summary>
    public (List<ArenaDeckCard> deck, List<string> energy) StageDeck(PveStage stage)
    {
        var cat = data.Catalog;
        var families = stage.Enemies.Select(e => cat.Form(e.FormId).FamilyId).Distinct().ToList();
        var forms = Rules.Cards.Where(c => c.Kind == "pet" && families.Contains(c.Species ?? "")).ToList();
        var elements = forms.Select(f => f.Element).Distinct().ToList();
        forms.AddRange(Rules.Cards.Where(c => c.Kind == "pet" && !c.Ex && elements.Contains(c.Element) && !families.Contains(c.Species ?? "")).OrderBy(c => c.Id, StringComparer.Ordinal).Take(9));
        long n = 1;
        var pool = forms.SelectMany(f => new[] { (f.Id, n++), (f.Id, n++) }).ToList();
        var trainers = Rules.Starter.ToDictionary(kv => kv.Key, kv => kv.Value);
        var entries = ArenaDecks.Auto(Rules, pool, trainers);
        var deck = ArenaDecks.Expand(entries).Select(d => new ArenaDeckCard { DefId = d.DefId }).ToList();
        return (deck, ArenaDecks.DefaultEnergy(Rules, deck.Select(d => d.DefId)));
    }

    public async Task EnsureCardVersion()
    {
        if (await db.ArenaCardVersions.AnyAsync(v => v.Version == Rules.Version)) return;
        db.ArenaCardVersions.Add(new ArenaCardVersion { Version = Rules.Version, DefinitionsJson = JsonSerializer.Serialize(Rules, J), CreatedAt = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); }   // created concurrently
    }

    // ------------------------------------------------------------------ matches

    public sealed record Side(long? Mid, string Name, List<ArenaDeckCard> Deck, List<string> Energy, bool Ai);

    public async Task<ArenaMatch> Start(int sid, string mode, Side a, Side b, string? stageId = null, int? seasonId = null, ArenaMatch? existing = null)
    {
        await EnsureCardVersion();
        long seed = SecureRandomSource.NewSeed();
        var st = ArenaEngine.Create(Rules, new ArenaSetupPlayer { Name = a.Name, Deck = a.Deck, EnergyTypes = a.Energy },
            new ArenaSetupPlayer { Name = b.Name, Deck = b.Deck, EnergyTypes = b.Energy }, unchecked((ulong)seed), pve: mode == "pve");
        var now = DateTimeOffset.UtcNow;
        var m = existing ?? new ArenaMatch { Id = Guid.NewGuid(), ServerId = sid, Mode = mode, CreatedAt = now };
        m.Status = "active"; m.DefsVersion = Rules.Version; m.Seed = seed; m.StageId = stageId; m.SeasonId = seasonId; m.StartedAt = now;
        if (existing == null) db.ArenaMatches.Add(m);
        else await db.ArenaMatchPlayers.Where(p => p.MatchId == m.Id).ExecuteDeleteAsync();
        foreach (var (s, side) in new[] { (a, 0), (b, 1) })
            db.ArenaMatchPlayers.Add(new ArenaMatchPlayer
            {
                MatchId = m.Id, Side = side, MembershipId = s.Mid, Name = s.Name, IsAi = s.Ai,
                DeckJson = JsonSerializer.Serialize(new ReplayDeck { Deck = s.Deck, EnergyTypes = s.Energy }, J),
            });
        await db.SaveChangesAsync();
        await RunAi(m, st);
        Save(m, st, now, true);
        await db.SaveChangesAsync();
        return m;
    }

    public sealed class ReplayDeck { public List<ArenaDeckCard> Deck { get; set; } = new(); public List<string> EnergyTypes { get; set; } = new(); }

    public static ArenaState Load(ArenaMatch m) => JsonSerializer.Deserialize<ArenaState>(m.StateJson, J)!;

    private async Task<int[]> AiSides(ArenaMatch m) =>
        (await db.ArenaMatchPlayers.AsNoTracking().Where(p => p.MatchId == m.Id && p.IsAi).Select(p => p.Side).ToListAsync()).ToArray();

    private async Task Record(ArenaMatch m, int side, string kind, object action, int versionAfter, string? actionId = null)
    {
        m.EventSeq++;
        db.ArenaEvents.Add(new ArenaEventRow { MatchId = m.Id, Seq = m.EventSeq, Side = side, Kind = kind, ActionId = actionId, ActionJson = JsonSerializer.Serialize(action, J), VersionAfter = versionAfter, At = DateTimeOffset.UtcNow });
        await Task.CompletedTask;
    }

    /// <summary>Lets every AI side act until a human must act (each AI action is logged like a player's).</summary>
    public async Task RunAi(ArenaMatch m, ArenaState st)
    {
        var ai = await AiSides(m);
        for (int guard = 0; guard < 600 && !st.Finished; guard++)
        {
            int side = ai.FirstOrDefault(s => ArenaEngine.MustAct(st, s), -1);
            if (side < 0) break;
            var a = ArenaAi.Decide(Rules, st, side);
            ArenaEngine.Apply(Rules, st, side, a);
            await Record(m, side, "action", a, st.Version);
        }
    }

    public DateTimeOffset? Deadline(ArenaState st, int[] ai, DateTimeOffset from)
    {
        if (st.Finished) return null;
        bool humanMustAct = Enumerable.Range(0, 2).Any(q => ArenaEngine.MustAct(st, q) && !ai.Contains(q));
        if (!humanMustAct) return null;
        return from.AddSeconds(st.Phase == "setup" ? Rules.Config.SetupSeconds : Rules.Config.TurnSeconds);
    }

    public void Save(ArenaMatch m, ArenaState st, DateTimeOffset now, bool resetDeadline, int[]? ai = null)
    {
        if (resetDeadline) m.Deadline = Deadline(st, ai ?? Array.Empty<int>(), now);
        m.StateJson = JsonSerializer.Serialize(st, J);
        m.Version = st.Version;
        m.UpdatedAt = now;
        if (m.EventSeq > 0 && (m.EventSeq % SnapshotEvery == 0 || st.Finished) && !db.ArenaSnapshots.Local.Any(s => s.MatchId == m.Id && s.Seq == m.EventSeq))
            db.ArenaSnapshots.Add(new ArenaSnapshot { MatchId = m.Id, Seq = m.EventSeq, StateJson = m.StateJson, At = now });
    }

    /// <summary>Applies every expired timer (auto-setup / auto-promote / end turn; repeated timeouts forfeit).</summary>
    public async Task<bool> ApplyTimeouts(ArenaMatch m, ArenaState st, DateTimeOffset now)
    {
        bool changed = false; int guard = 0;
        var ai = await AiSides(m);
        while (!st.Finished && m.Deadline is DateTimeOffset dl && dl < now && guard++ < 12)
        {
            ArenaEngine.Timeout(Rules, st);
            await Record(m, -1, "timeout", new { }, st.Version);
            await RunAi(m, st);
            m.Deadline = Deadline(st, ai, dl);
            changed = true;
        }
        return changed;
    }

    /// <summary>One player action under the caller's row lock: idempotent by actionId, refused on a stale version.</summary>
    public async Task<(ArenaState st, bool duplicate)> Act(ArenaMatch m, int side, string? actionId, int version, ArenaAction action)
    {
        var st = Load(m);
        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrEmpty(actionId) && await db.ArenaEvents.AnyAsync(e => e.MatchId == m.Id && e.ActionId == actionId))
            return (st, true);                                                   // a retried request: already applied, return the current view
        bool timedOut = await ApplyTimeouts(m, st, now);
        if (!st.Finished)
        {
            if (action.Type != "surrender" && version != st.Version)
            {
                if (timedOut) { Save(m, st, now, false); if (st.Finished) await Finish(m, st); await db.SaveChangesAsync(); }
                throw new ArenaStaleException(timedOut);
            }
            try { ArenaEngine.Apply(Rules, st, side, action); }
            catch (ArenaRuleException e) { throw ApiException.Conflict(e.Code); }
            st.Players[side].Timeouts = 0;
            await Record(m, side, "action", action, st.Version, actionId);
            await RunAi(m, st);
        }
        Save(m, st, now, true, await AiSides(m));
        if (st.Finished) await Finish(m, st);
        return (st, false);
    }

    public sealed class ArenaStaleException(bool saved) : Exception("stale_state") { public bool Saved { get; } = saved; }

    // ------------------------------------------------------------------ views

    /// <summary>What one side may see: its own hand; never the opponent's hand, any deck order, the opponent's face-down
    /// setup choice or the RNG state. Discards and Pets in play are public.</summary>
    public static ArenaState View(ArenaState st, int side)
    {
        var v = JsonSerializer.Deserialize<ArenaState>(JsonSerializer.Serialize(st, J), J)!;
        v.Rng = 0;
        var visible = new HashSet<int>();
        for (int p = 0; p < 2; p++)
        {
            var pl = v.Players[p];
            bool hideBoard = v.Phase == "setup" && p != side;                    // setup choices are secret until both are done
            if (hideBoard) { pl.Active = null; pl.Bench = new(); }
            visible.UnionWith(pl.Discard);
            foreach (var pet in pl.InPlay()) { visible.UnionWith(pet.Cards); if (pet.Tool != 0) visible.Add(pet.Tool); }
            if (p == side) visible.UnionWith(pl.Hand);
            else pl.Hand = pl.Hand.Select(_ => 0).ToList();
            pl.Deck = pl.Deck.Select(_ => 0).ToList();
        }
        foreach (var c in v.Cards) if (!visible.Contains(c.Id)) { c.DefId = ""; c.CardInstanceId = 0; }
        return v;
    }

    public async Task<ArenaViewDto> Dto(ArenaMatch m, ArenaState st, long mid)
    {
        var players = await db.ArenaMatchPlayers.AsNoTracking().Where(p => p.MatchId == m.Id).ToListAsync();
        int you = players.FirstOrDefault(p => p.MembershipId == mid)?.Side ?? 0;
        var opp = players.FirstOrDefault(p => p.Side == 1 - you)?.Name;
        var rewards = m.Status == "finished" ? await db.ArenaRewards.AsNoTracking().Where(r => r.MatchId == m.Id && r.MembershipId == mid).Select(r => r.RewardsJson).FirstOrDefaultAsync() : null;
        return new ArenaViewDto(m.Id, m.Mode, m.Status, you, st.Version, m.Deadline, DateTimeOffset.UtcNow, opp, View(st, you), m.RoomCode, rewards);
    }

    public async Task Push(ArenaMatch m, ArenaState st, long? except = null)
    {
        var humans = await db.ArenaMatchPlayers.AsNoTracking().Where(p => p.MatchId == m.Id && p.MembershipId != null).Select(p => p.MembershipId!.Value).ToListAsync();
        var to = humans.Where(h => h != except).ToList();
        if (to.Count > 0) await hub.ToMembers(to, "arena_update", new { matchId = m.Id, version = st.Version, status = m.Status });
    }

    // ------------------------------------------------------------------ results

    /// <summary>Finishes a match inside the caller's transaction: status, ratings (ranked), PvE rewards with the existing
    /// daily caps, achievements. arena_rewards has a unique (match, player) key, so a reward can never be paid twice.</summary>
    public async Task Finish(ArenaMatch m, ArenaState st)
    {
        if (m.Status == "finished") return;
        m.Status = "finished"; m.WinnerSide = st.Winner; m.EndReason = st.EndReason; m.FinishedAt = DateTimeOffset.UtcNow; m.Deadline = null;
        var players = await db.ArenaMatchPlayers.Where(p => p.MatchId == m.Id).OrderBy(p => p.Side).ToListAsync();
        if (m.Mode == "ranked" && m.SeasonId is int season && players.All(p => p.MembershipId != null))
        {
            var r = new ArenaRating[2];
            for (int s = 0; s < 2; s++)
            {
                long mid = players[s].MembershipId!.Value;
                r[s] = await db.ArenaRatings.FirstOrDefaultAsync(x => x.SeasonId == season && x.MembershipId == mid)
                       ?? db.ArenaRatings.Add(new ArenaRating { SeasonId = season, MembershipId = mid, ServerId = m.ServerId }).Entity;
            }
            double ea = 1.0 / (1 + Math.Pow(10, (r[1].Rating - r[0].Rating) / 400.0));
            double sa = st.Winner == 0 ? 1 : st.Winner == 1 ? 0 : 0.5;
            int delta = (int)Math.Round(32 * (sa - ea));
            for (int s = 0; s < 2; s++)
            {
                players[s].RatingBefore = r[s].Rating;
                r[s].Rating = Math.Max(0, r[s].Rating + (s == 0 ? delta : -delta));
                players[s].RatingAfter = r[s].Rating;
                if (st.Winner == null) r[s].Draws++; else if (st.Winner == s) r[s].Wins++; else r[s].Losses++;
                r[s].UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        foreach (var p in players.Where(p => p.MembershipId != null))
        {
            long mid = p.MembershipId!.Value;
            object rewards = new { };
            if (m.Mode == "pve" && st.Winner == p.Side && m.StageId != null)
                rewards = await PveRewards(m, mid, data.Pve.Stages.First(s => s.Id == m.StageId), st);
            db.ArenaRewards.Add(new ArenaReward { MatchId = m.Id, MembershipId = mid, RewardsJson = JsonSerializer.Serialize(new { result = st.Winner == null ? "draw" : st.Winner == p.Side ? "win" : "loss", ratingBefore = p.RatingBefore, ratingAfter = p.RatingAfter, rewards }, J), At = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
        if (st.Winner is int w && players[w].MembershipId is long winner) await ach.Evaluate(winner, m.ServerId, "duel_win");
    }

    /// <summary>Stage rewards for a PvE win: the same stage tables and daily cap as the existing PvE battles (shared counter).</summary>
    private async Task<Dictionary<string, object>> PveRewards(ArenaMatch m, long mid, PveStage stage, ArenaState st)
    {
        var res = new Dictionary<string, object>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var p = await db.PveProgress.FirstOrDefaultAsync(x => x.MembershipId == mid && x.StageId == stage.Id);
        if (p == null) { p = new PveProgress { MembershipId = mid, StageId = stage.Id, DailyDate = today }; db.PveProgress.Add(p); }
        if (p.DailyDate != today) { p.DailyDate = today; p.DailyRewardedClears = 0; }
        bool first = p.ClearCount == 0;
        p.ClearCount++; p.FirstClearedAt ??= DateTimeOffset.UtcNow;
        var grants = new List<PveRewards>();
        if (p.DailyRewardedClears < data.Pve.DailyRewardedClearsPerStage) { grants.Add(stage.Rewards); p.DailyRewardedClears++; } else res["dailyCapReached"] = true;
        if (first) { grants.Add(stage.FirstClearRewards); res["firstClear"] = true; }
        var currencies = new Dictionary<string, long>(); var items = new Dictionary<string, long>(); var packs = new Dictionary<string, long>();
        foreach (var (gr, idx) in grants.Select((g, i) => (g, i)))
        {
            string r = $"arena:{m.Id:N}:{idx}";
            foreach (var (cur, amt) in gr.Currencies) { await eco.Currency(mid, m.ServerId, cur, amt, "arena_pve_reward", r); currencies[cur] = currencies.GetValueOrDefault(cur) + amt; }
            foreach (var (item, amt) in gr.Items) { await eco.Item(mid, m.ServerId, item, amt, "arena_pve_reward", r); items[item] = items.GetValueOrDefault(item) + amt; }
            foreach (var (pack, amt) in gr.Packs) { await eco.Packs(mid, m.ServerId, pack, amt, "arena_pve_reward", r); packs[pack] = packs.GetValueOrDefault(pack) + amt; }
        }
        res["currencies"] = currencies; res["items"] = items; res["packs"] = packs;
        return res;
    }

    // ------------------------------------------------------------------ replay

    public sealed class ReplayDto
    {
        public Guid MatchId { get; set; }
        public string Mode { get; set; } = "";
        public string DefsVersion { get; set; } = "";
        public long Seed { get; set; }
        public List<string> Names { get; set; } = new();
        public List<ReplayDeck> Decks { get; set; } = new();
        public List<ReplayEvent> Events { get; set; } = new();
        public int? Winner { get; set; }
        public string? EndReason { get; set; }
    }
    public sealed class ReplayEvent { public int Seq { get; set; } public int Side { get; set; } public string Kind { get; set; } = ""; public ArenaAction Action { get; set; } = new(); }

    public async Task<ReplayDto> Replay(ArenaMatch m)
    {
        var players = await db.ArenaMatchPlayers.AsNoTracking().Where(p => p.MatchId == m.Id).OrderBy(p => p.Side).ToListAsync();
        var events = await db.ArenaEvents.AsNoTracking().Where(e => e.MatchId == m.Id).OrderBy(e => e.Seq).ToListAsync();
        return new ReplayDto
        {
            MatchId = m.Id, Mode = m.Mode, DefsVersion = m.DefsVersion, Seed = m.Seed, Winner = m.WinnerSide, EndReason = m.EndReason,
            Names = players.Select(p => p.Name).ToList(),
            Decks = players.Select(p => JsonSerializer.Deserialize<ReplayDeck>(p.DeckJson, J)!).ToList(),
            Events = events.Select(e => new ReplayEvent { Seq = e.Seq, Side = e.Side, Kind = e.Kind, Action = e.Kind == "timeout" ? new ArenaAction() : JsonSerializer.Deserialize<ArenaAction>(e.ActionJson, J)! }).ToList(),
        };
    }

    /// <summary>Rebuilds a match from its replay with the card definitions of its own version (not today's).</summary>
    public async Task<ArenaState> Rebuild(ReplayDto r)
    {
        var defsJson = await db.ArenaCardVersions.AsNoTracking().Where(v => v.Version == r.DefsVersion).Select(v => v.DefinitionsJson).FirstAsync();
        var rules = JsonSerializer.Deserialize<ArenaRules>(defsJson, J)!;
        var st = ArenaEngine.Create(rules, new ArenaSetupPlayer { Name = r.Names[0], Deck = r.Decks[0].Deck, EnergyTypes = r.Decks[0].EnergyTypes },
            new ArenaSetupPlayer { Name = r.Names[1], Deck = r.Decks[1].Deck, EnergyTypes = r.Decks[1].EnergyTypes }, unchecked((ulong)r.Seed), pve: r.Mode == "pve");
        foreach (var e in r.Events)
        {
            if (e.Kind == "timeout") ArenaEngine.Timeout(rules, st);
            else ArenaEngine.Apply(rules, st, e.Side, e.Action);
        }
        return st;
    }
}
