using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Features;
using PetStar.Rules;
using PetStar.Rules.Battle;
using PetStar.Rules.Duel;

namespace PetStar.Api.Infrastructure;

/// <summary>Duel rules built once from the catalog + duel_cards.json.</summary>
public sealed class DuelRulesProvider(GameData data, DuelData duel)
{
    public DuelRules Rules { get; } = DuelBuilder.Rules(data.Catalog, duel.Config, duel.Tactics);
    public DuelData Data { get; } = duel;
}

public sealed class DeckEntryJson
{
    public string Kind { get; set; } = "pet";
    public long? CardId { get; set; }
    public string? TacticId { get; set; }
    public int Count { get; set; } = 1;
}

public sealed record DuelViewDto(Guid Id, string Mode, string Status, int You, int Version, DateTimeOffset? DeadlineAt, DateTimeOffset ServerNow,
    string OpponentName, DuelState State, string? RewardsJson);

/// <summary>
/// Server side of the Tactical Duel: builds decks from what the player really owns, runs the authoritative engine,
/// controls AI sides, enforces timers, hides private information (hands, decks, set cards, RNG) and records results
/// in the battles table (so missions, achievements, profile history and ranked keep working).
/// </summary>
public sealed class DuelService(PetStarDb db, GameData data, DuelRulesProvider rp, EconomyService eco)
{
    private static readonly JsonSerializerOptions J = IdempotencyService.Json;
    public DuelRules Rules => rp.Rules;

    // ------------------------------------------------------------------ collection & decks

    /// <summary>Grants the starter Spell/Trap collection once (ledger reference makes it idempotent). Needs a transaction.</summary>
    public async Task EnsureStarter(long mid, int sid)
    {
        foreach (var (id, n) in rp.Data.Starter.OrderBy(x => x.Key))
            await eco.Item(mid, sid, id, n, "starter_tactics", "starter_tactics", tolerateDuplicate: true);
    }

    public async Task<Dictionary<string, long>> OwnedTactics(long mid)
    {
        var ids = Rules.Tactics.Keys.ToList();
        return await db.Items.AsNoTracking().Where(i => i.MembershipId == mid && ids.Contains(i.ItemId) && i.Quantity > 0).ToDictionaryAsync(i => i.ItemId, i => i.Quantity);
    }

    public static List<DeckEntryJson> Entries(string json) => JsonSerializer.Deserialize<List<DeckEntryJson>>(json, J) ?? new();

    /// <summary>Validates ownership, locks and the 40-card rules; returns the engine deck. Throws ApiException codes.</summary>
    public async Task<List<DuelCard>> BuildDeck(long mid, int sid, List<DeckEntryJson> entries, bool ranked)
    {
        var cat = data.Catalog;
        var petIds = entries.Where(e => e.Kind == "pet" && e.CardId != null).Select(e => e.CardId!.Value).ToList();
        var cards = await db.Cards.AsNoTracking().Where(c => petIds.Contains(c.Id) && c.OwnerMembershipId == mid && c.ServerId == sid).ToDictionaryAsync(c => c.Id);
        if (cards.Count != petIds.Distinct().Count()) throw ApiException.Conflict("deck_card_not_owned");
        if (cards.Values.Any(c => c.BusyState != null)) throw ApiException.Conflict("deck_card_busy");
        var owned = await OwnedTactics(mid);
        foreach (var g in entries.Where(e => e.Kind == "tactic").GroupBy(e => e.TacticId ?? ""))
            if (owned.GetValueOrDefault(g.Key) < g.Sum(e => e.Count)) throw ApiException.Conflict("deck_tactic_not_owned");
        var info = entries.Select(e => e.Kind == "pet"
            ? new DeckEntryInfo { Kind = "pet", CardInstanceId = e.CardId ?? 0, FormId = cards.TryGetValue(e.CardId ?? 0, out var c) ? c.FormId : "", Divine = cards.TryGetValue(e.CardId ?? 0, out var c2) && cat.Form(c2.FormId).DivineSet != null }
            : new DeckEntryInfo { Kind = "tactic", TacticId = e.TacticId, Count = e.Count }).ToList();
        var err = DuelDeckRules.Validate(Rules, info);
        if (err != null) throw ApiException.Conflict(err);
        var engr = await CardProgressLoader.Engravings(db, cards.Keys);
        var deck = new List<DuelCard>();
        foreach (var e in entries)
        {
            if (e.Kind == "pet")
            {
                var card = cards[e.CardId!.Value];
                deck.Add(new DuelCard
                {
                    Kind = "pet", CardInstanceId = card.Id,
                    Forms = DuelBuilder.PetForms(cat, Rules.Config, card.FormId, card.Rarity, card.Quality, CardProgressLoader.Progress(card, engr.GetValueOrDefault(card.Id)), ranked),
                });
            }
            else for (int i = 0; i < e.Count; i++) deck.Add(new DuelCard { Kind = "tactic", TacticId = e.TacticId });
        }
        return deck;
    }

    /// <summary>Builds a legal deck from the player's strongest free Pets and owned Spells/Traps.</summary>
    public async Task<List<DeckEntryJson>> AutoDeck(long mid, int sid)
    {
        var cat = data.Catalog;
        var cards = await db.Cards.AsNoTracking().Where(c => c.OwnerMembershipId == mid && c.ServerId == sid && c.BusyState == null).ToListAsync();
        var picked = new List<CardInstance>();
        foreach (var c in cards.OrderByDescending(c => data.ToDto(c).Power).ThenBy(c => c.Id))
        {
            if (picked.Count >= 18) break;
            bool divine = cat.Form(c.FormId).DivineSet != null;
            if (picked.Count(x => x.FormId == c.FormId) >= (divine ? Rules.Config.DivineMaxCopies : Rules.Config.MaxCopies)) continue;
            picked.Add(c);
        }
        if (picked.Count < Rules.Config.PetMin) throw ApiException.Conflict("not_enough_pets");
        var entries = picked.Select(c => new DeckEntryJson { Kind = "pet", CardId = c.Id }).ToList();
        var owned = await OwnedTactics(mid);
        int need = Rules.Config.DeckSize - picked.Count;
        var counts = new Dictionary<string, int>();
        for (int round = 0; round < Rules.Config.MaxCopies && need > 0; round++)
            foreach (var t in Rules.Tactics.Keys.OrderBy(k => k))
            {
                if (need == 0) break;
                if (owned.GetValueOrDefault(t) > counts.GetValueOrDefault(t)) { counts[t] = counts.GetValueOrDefault(t) + 1; need--; }
            }
        if (need > 0)
        {   // not enough Spells/Traps: add more owned Pets instead (up to 25)
            foreach (var c in cards.Except(picked).OrderByDescending(c => data.ToDto(c).Power).ThenBy(c => c.Id))
            {
                if (need == 0 || entries.Count >= Rules.Config.PetMax) break;
                bool divine = cat.Form(c.FormId).DivineSet != null;
                if (entries.Count(x => x.CardId != null && cards.First(y => y.Id == x.CardId).FormId == c.FormId) >= (divine ? 1 : 3)) continue;
                entries.Add(new DeckEntryJson { Kind = "pet", CardId = c.Id }); need--;
            }
            if (need > 0) throw ApiException.Conflict("not_enough_cards");
        }
        entries.AddRange(counts.OrderBy(k => k.Key).Select(k => new DeckEntryJson { Kind = "tactic", TacticId = k.Key, Count = k.Value }));
        return entries;
    }

    /// <summary>AI deck for a PvE stage: the stage's Pets (3 copies each, filled with same-element first forms) and starter tactics.</summary>
    public List<DuelCard> StageDeck(PveStage stage)
    {
        var cat = data.Catalog;
        var forms = stage.Enemies.Select(e => e.FormId).Distinct().ToList();
        var lvl = stage.Enemies.Max(e => e.Level);
        var pets = new List<(string form, PveEnemy e)>();
        foreach (var f in forms) for (int i = 0; i < 3; i++) pets.Add((f, stage.Enemies.First(x => x.FormId == f)));
        var elements = forms.Select(f => cat.Form(f).Element).ToHashSet();
        foreach (var f in cat.Forms.Values.Where(f => f.DivineSet == null && f.Stage == 1 && elements.Contains(f.Element) && !forms.Contains(f.Id)).OrderBy(f => f.Id))
        {
            if (pets.Count >= 15) break;
            for (int i = 0; i < 2 && pets.Count < 15; i++) pets.Add((f.Id, new PveEnemy { FormId = f.Id, Level = lvl, Rarity = "N", Quality = "C" }));
        }
        var deck = pets.Select(p => new DuelCard
        {
            Kind = "pet", Forms = DuelBuilder.PetForms(cat, Rules.Config, p.form, p.e.Rarity, p.e.Quality, new CardProgress { Level = Math.Max(1, p.e.Level) }, false),
        }).ToList();
        foreach (var (id, n) in rp.Data.Starter.OrderBy(k => k.Key))
            for (int i = 0; i < Math.Min(3, n) && deck.Count < Rules.Config.DeckSize; i++) deck.Add(new DuelCard { Kind = "tactic", TacticId = id });
        return deck;
    }

    // ------------------------------------------------------------------ state, AI, timers

    public static DuelState Load(Battle b) => JsonSerializer.Deserialize<DuelState>(b.StateJson, J)!;
    public static int AiSide(Battle b) => b.Mode == "live_duel" ? -1 : 1;

    public void RunAi(Battle b, DuelState st)
    {
        int ai = AiSide(b), n = 0;
        while (ai >= 0 && !st.Finished && st.ToAct == ai && n++ < 400) DuelEngine.Apply(Rules, st, ai, DuelAi.Decide(Rules, st, ai));
        if (ai >= 0 && !st.Finished && st.ToAct == ai) DuelEngine.Apply(Rules, st, ai, new DuelAction { Type = "surrender" });   // runaway guard (never expected)
    }

    public DateTimeOffset? Deadline(Battle b, DuelState st, DateTimeOffset from)
    {
        if (st.Finished || st.ToAct == AiSide(b)) return null;
        return from.AddSeconds(st.Priority >= 0 ? Rules.Config.ReactionSeconds : Rules.Config.TurnSeconds);
    }

    /// <summary>Applies every expired timer (auto-pass / end turn; repeated timeouts forfeit). Returns true if anything changed.</summary>
    public bool ApplyTimeouts(Battle b, DuelState st, DateTimeOffset now)
    {
        bool changed = false;
        int guard = 0;
        while (!st.Finished && b.RoundDeadline is DateTimeOffset dl && dl < now && guard++ < 12)
        {
            DuelEngine.Timeout(Rules, st);
            RunAi(b, st);
            b.RoundDeadline = Deadline(b, st, dl);
            changed = true;
        }
        return changed;
    }

    public void Save(Battle b, DuelState st, DateTimeOffset now, bool resetDeadline)
    {
        if (resetDeadline) b.RoundDeadline = Deadline(b, st, now);
        b.StateJson = JsonSerializer.Serialize(st, J);
        b.Round = st.Turn;
        b.UpdatedAt = now;
    }

    // ------------------------------------------------------------------ hidden information

    /// <summary>What one side may see: own hand; never the opponent's hand, any deck order, face-down cards or the RNG.</summary>
    public static DuelState View(DuelState st, int side)
    {
        var v = JsonSerializer.Deserialize<DuelState>(JsonSerializer.Serialize(st, J), J)!;
        v.Rng = 0;
        var visible = new HashSet<int>();
        for (int p = 0; p < 2; p++)
        {
            var pl = v.Players[p];
            visible.UnionWith(pl.Grave);
            visible.UnionWith(pl.Units.Select(u => u.CardId));
            if (p == side) { visible.UnionWith(pl.Hand); visible.UnionWith(pl.Zones.Where(z => z != null).Select(z => z!.CardId)); }
            else
            {
                pl.Hand = pl.Hand.Select(_ => 0).ToList();
                for (int z = 0; z < pl.Zones.Count; z++) if (pl.Zones[z] != null) pl.Zones[z] = new SetCard { CardId = 0, TacticId = "", SetTurn = pl.Zones[z]!.SetTurn };
            }
            pl.Deck = pl.Deck.Select(_ => 0).ToList();
        }
        visible.UnionWith(v.Chain.Where(c => c.CardId > 0).Select(c => c.CardId));
        for (int i = 0; i < v.Cards.Count; i++)
            if (!visible.Contains(v.Cards[i].Id)) v.Cards[i] = new DuelCard { Id = v.Cards[i].Id, Kind = "hidden" };
        return v;
    }

    public async Task<DuelViewDto> Dto(Battle b, DuelState st, long mid)
    {
        int you = b.PlayerAMembershipId == mid ? 0 : 1;
        string opp = you == 0 ? st.Players[1].Name : st.Players[0].Name;
        string? rewards = b.Status == "finished" ? await db.BattleResults.AsNoTracking().Where(r => r.BattleId == b.Id).Select(r => r.RewardsJson).FirstOrDefaultAsync() : null;
        return new DuelViewDto(b.Id, b.Mode, b.Status, you, st.Version, b.RoundDeadline, DateTimeOffset.UtcNow, opp, View(st, you), rewards);
    }

    // ------------------------------------------------------------------ results

    /// <summary>Records the result when a duel ends: PvE rewards (same tables and daily caps as before), ranked rating,
    /// PvP result; all through the existing battle result paths so missions and achievements count it.</summary>
    public async Task Finish(Battle b, DuelState st, AchievementService ach)
    {
        b.Status = "finished"; b.WinnerSide = st.Winner; b.EndReason = st.EndReason; b.FinishedAt = DateTimeOffset.UtcNow; b.RoundDeadline = null;
        var shim = new BattleState { Winner = st.Winner, Round = st.Turn };
        foreach (var c in st.Cards.Where(c => c.Kind == "pet" && st.Players[0].Deck.Concat(st.Players[0].Hand).Concat(st.Players[0].Grave).Concat(st.Players[0].Units.Select(u => u.CardId)).Contains(c.Id) && c.CardInstanceId > 0).Select(c => c.CardInstanceId).Distinct().Take(5))
            shim.Units.Add(new BattleUnit { Side = 0, CardId = c });
        await db.SaveChangesAsync();
        if (b.Mode == "duel_pve")
        {
            var stage = data.Pve.Stages.First(s => s.Id == b.StageId);
            var rewards = await BattleEndpoints.FinishPve(db, eco, data, b, shim, stage, b.ServerId, b.PlayerAMembershipId, ach);
            var row = db.BattleResults.Local.FirstOrDefault(r => r.BattleId == b.Id);
            if (row != null) row.RewardsJson = JsonSerializer.Serialize(rewards, J);
        }
        else if (b.Mode == "ranked")
        {
            var rewards = await RankedEndpoints.Finish(db, eco, ach, b, shim, b.ServerId, b.PlayerAMembershipId);
            var row = db.BattleResults.Local.FirstOrDefault(r => r.BattleId == b.Id);
            if (row != null) row.RewardsJson = JsonSerializer.Serialize(rewards, J);
        }
        else
        {
            long? winner = st.Winner == 0 ? b.PlayerAMembershipId : st.Winner == 1 ? b.PlayerBMembershipId : null;
            db.BattleResults.Add(new BattleResult { BattleId = b.Id, WinnerMembershipId = winner, RewardsJson = "{}", At = DateTimeOffset.UtcNow });
            if (winner is long w) await ach.Evaluate(w, b.ServerId, "duel_win");
        }
        await db.SaveChangesAsync();
    }
}
