using System.Collections.Generic;
using System.Linq;

namespace PetStar.Rules.Arena
{
    /// <summary>One deck line: a Pet card instance (count 1) or a trainer definition with a count.</summary>
    public sealed class ArenaDeckEntry
    {
        public string DefId { get; set; } = "";
        public long CardInstanceId { get; set; }
        public int Count { get; set; } = 1;
    }

    public static class ArenaDecks
    {
        /// <summary>Legality only (ownership and locks are checked by the server against the database).
        /// Returns null when legal, otherwise a readable error code.</summary>
        public static string? Validate(ArenaRules rules, IList<ArenaDeckEntry> entries, IList<string>? energyTypes)
        {
            var cfg = rules.Config;
            if (entries.Any(e => e.Count <= 0)) return "deck_invalid_count";
            if (entries.Any(e => !rules.Has(e.DefId))) return "deck_unknown_card";
            if (entries.Sum(e => e.Count) != cfg.DeckSize) return "deck_must_have_20";
            var pets = entries.Where(e => rules.Def(e.DefId).Kind == "pet").ToList();
            if (pets.Any(e => e.Count != 1 || e.CardInstanceId <= 0)) return "deck_pet_needs_instance";
            if (pets.GroupBy(e => e.CardInstanceId).Any(g => g.Count() > 1)) return "deck_duplicate_instance";
            if (entries.GroupBy(e => e.DefId).Any(g => g.Sum(x => x.Count) > cfg.MaxCopies)) return "deck_too_many_copies";
            if (!pets.Any(e => rules.Def(e.DefId).IsBasic)) return "deck_needs_basic";
            if (energyTypes != null)
            {
                if (energyTypes.Count == 0 || energyTypes.Count > cfg.MaxEnergyTypes || energyTypes.Distinct().Count() != energyTypes.Count) return "deck_energy_types";
                if (energyTypes.Any(t => !rules.Elements.Contains(t))) return "deck_energy_types";
            }
            return null;
        }

        /// <summary>Energy Zone types when the player did not choose: the most needed attack types (max 3).</summary>
        public static List<string> DefaultEnergy(ArenaRules rules, IEnumerable<string> defIds)
        {
            var need = new Dictionary<string, int>();
            foreach (var id in defIds)
            {
                var d = rules.Def(id);
                if (d.Kind != "pet") continue;
                foreach (var c in d.Attacks.SelectMany(a => a.Cost).Where(c => c != "colorless"))
                    need[c] = need.TryGetValue(c, out var n) ? n + 1 : 1;
            }
            var list = need.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, System.StringComparer.Ordinal).Select(kv => kv.Key).Take(rules.Config.MaxEnergyTypes).ToList();
            return list.Count > 0 ? list : new List<string> { rules.Elements.FirstOrDefault() ?? "colorless" };
        }

        public static List<ArenaDeckCard> Expand(IEnumerable<ArenaDeckEntry> entries) =>
            entries.SelectMany(e => Enumerable.Repeat(new ArenaDeckCard { DefId = e.DefId, CardInstanceId = e.CardInstanceId }, e.Count)).ToList();

        /// <summary>A legal deck from available Pet forms (id + card instance) and trainer counts: the strongest evolution
        /// lines first (Basic + its evolutions), up to 2 copies, then trainers. Used for auto-decks, AI decks and tests.</summary>
        public static List<ArenaDeckEntry> Auto(ArenaRules rules, IList<(string defId, long instance)> pets, IDictionary<string, int> trainers, int petTarget = 12)
        {
            var cfg = rules.Config;
            var deck = new List<ArenaDeckEntry>();
            int Count(string id) => deck.Where(e => e.DefId == id).Sum(e => e.Count);
            var byDef = pets.GroupBy(p => p.defId).ToDictionary(g => g.Key, g => g.Select(x => x.instance).ToList());
            int Score(string id) { var d = rules.Def(id); return d.Hp + d.Attacks.Select(a => a.Damage).DefaultIfEmpty(0).Max() * 2 + (d.Ex ? 80 : 0); }
            // lines: a Basic and the evolutions we own
            var basics = byDef.Keys.Where(id => rules.Def(id).IsBasic).OrderByDescending(id => LineScore(id)).ThenBy(id => id, System.StringComparer.Ordinal).ToList();
            int LineScore(string basic)
            {
                int s = Score(basic); var cur = basic;
                for (int depth = 0; depth < 2; depth++)
                {
                    var next = byDef.Keys.FirstOrDefault(id => rules.Def(id).EvolvesFrom == cur);
                    if (next == null) break; s += Score(next); cur = next;
                }
                return s;
            }
            void Add(string id, int n)
            {
                var inst = byDef[id];
                for (int i = 0; i < n && Count(id) < cfg.MaxCopies && deck.Count(e => rules.Def(e.DefId).Kind == "pet") < petTarget; i++)
                {
                    var free = inst.FirstOrDefault(x => !deck.Any(e => e.CardInstanceId == x));
                    if (free == 0) return;
                    deck.Add(new ArenaDeckEntry { DefId = id, CardInstanceId = free, Count = 1 });
                }
            }
            foreach (var b in basics)
            {
                if (deck.Count >= petTarget) break;
                Add(b, 2);
                var cur = b;
                for (int depth = 0; depth < 2; depth++)
                {
                    var next = byDef.Keys.Where(id => rules.Def(id).EvolvesFrom == cur).OrderBy(id => id, System.StringComparer.Ordinal).FirstOrDefault();
                    if (next == null) break;
                    Add(next, depth == 0 ? 2 : 1); cur = next;
                }
            }
            foreach (var t in trainers.OrderBy(kv => TrainerOrder(kv.Key)).ThenBy(kv => kv.Key, System.StringComparer.Ordinal))
            {
                int room = cfg.DeckSize - deck.Sum(e => e.Count);
                int n = System.Math.Min(System.Math.Min(t.Value, cfg.MaxCopies), room);
                if (n > 0 && rules.Has(t.Key)) deck.Add(new ArenaDeckEntry { DefId = t.Key, Count = n });
            }
            // still short (few trainers): fill with more Pets, ignoring the target
            if (deck.Sum(e => e.Count) < cfg.DeckSize)
                foreach (var id in byDef.Keys.OrderByDescending(Score).ThenBy(x => x, System.StringComparer.Ordinal))
                    foreach (var inst in byDef[id])
                    {
                        if (deck.Sum(e => e.Count) >= cfg.DeckSize) break;
                        if (Count(id) < cfg.MaxCopies && !deck.Any(e => e.CardInstanceId == inst)) deck.Add(new ArenaDeckEntry { DefId = id, CardInstanceId = inst, Count = 1 });
                    }
            return deck;
        }

        private static int TrainerOrder(string id) => id switch
        {
            "su_scholar" => 0, "at_star_lantern" => 1, "at_herb_salve" => 2, "su_medic" => 3, "su_captain" => 4, "su_guide" => 5,
            "at_leaf_armor" => 6, "su_strategist" => 7, "at_wind_boots" => 8, _ => 9,
        };
    }
}
