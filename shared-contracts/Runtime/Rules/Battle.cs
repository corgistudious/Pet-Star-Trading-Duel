using System;
using System.Collections.Generic;
using System.Linq;

namespace PetStar.Rules.Battle
{
    public enum ActionType { Basic = 0, Skill = 1, Ultimate = 2, Defend = 3 }

    public sealed class PlannedAction
    {
        public int UnitIndex { get; set; }
        public ActionType Type { get; set; }
        /// <summary>Target unit index (enemy side for offensive skills, own side for ally_single). Optional.</summary>
        public int? TargetIndex { get; set; }
    }

    public sealed class StatusInstance
    {
        public string Type { get; set; } = "";
        public int Remaining { get; set; }
        public bool Fresh { get; set; }
    }

    public sealed class BattleUnit
    {
        public int Side { get; set; }
        public int Index { get; set; }
        public int Position { get; set; }
        public long CardId { get; set; }
        public string FormId { get; set; } = "";
        public string Element { get; set; } = "";
        public int Level { get; set; }
        public int SkillLevel { get; set; } = 1;
        public string Rarity { get; set; } = "N";
        public int MaxHp { get; set; }
        public int Hp { get; set; }
        public int Atk { get; set; }
        public int Def { get; set; }
        public int Spd { get; set; }
        public double Crit { get; set; }
        public int Shield { get; set; }
        public bool Defending { get; set; }
        public string? SkillId { get; set; }
        public string? UltimateId { get; set; }
        public string? PassiveId { get; set; }
        public List<StatusInstance> Statuses { get; set; } = new List<StatusInstance>();

        public bool Alive => Hp > 0;
        public bool IsFront => Position < 3;
        public bool Has(string status) => Statuses.Any(s => s.Type == status && s.Remaining > 0);
    }

    public sealed class BattleEvent
    {
        public string Type { get; set; } = "";
        public int Round { get; set; }
        public int ActorSide { get; set; } = -1;
        public int ActorIndex { get; set; } = -1;
        public int TargetSide { get; set; } = -1;
        public int TargetIndex { get; set; } = -1;
        public int Amount { get; set; }
        public string? SkillId { get; set; }
        public string? Status { get; set; }
        public bool Crit { get; set; }
        public double ElementMultiplier { get; set; } = 1.0;
    }

    public sealed class BattleConfig
    {
        public int StartEnergy { get; set; } = 3;
        public int EnergyPerRound { get; set; } = 3;
        public int MaxEnergy { get; set; } = 10;
        public int MaxRounds { get; set; } = 20;
        public double CritMultiplier { get; set; } = 1.5;
        public double DefendMultiplier { get; set; } = 0.5;
        public double DefenseConstant { get; set; } = 100;
        public double BurnPct { get; set; } = 0.06;
        public double PoisonPct { get; set; } = 0.08;
        public double WeakenAtk { get; set; } = 0.75;
        public int DefendPriority { get; set; } = 3;
        /// <summary>Skill/ultimate power bonus per skill level above 1 (max level 5 → +24%).</summary>
        public double SkillLevelBonus { get; set; } = 0.06;
        /// <summary>3+ cards of one element: +ATK for those cards.</summary>
        public double ElementSynergyAtk { get; set; } = 0.06;
        /// <summary>4+ different elements: +HP for the whole team.</summary>
        public double HarmonySynergyHp { get; set; } = 0.05;
        /// <summary>Guardian in front and Support in back: +DEF for the whole team.</summary>
        public double FormationSynergyDef { get; set; } = 0.05;
    }

    public sealed class BattleState
    {
        public int Round { get; set; } = 1;
        public int[] Energy { get; set; } = new int[2];
        public List<BattleUnit> Units { get; set; } = new List<BattleUnit>();
        public bool Finished { get; set; }
        /// <summary>0 or 1 = winning side, -1 = draw, null = ongoing.</summary>
        public int? Winner { get; set; }
        public string? EndReason { get; set; }
        public ulong RngState { get; set; }
        public BattleConfig Config { get; set; } = new BattleConfig();
        public List<string> SynergiesA { get; set; } = new List<string>();
        public List<string> SynergiesB { get; set; } = new List<string>();

        public IEnumerable<BattleUnit> Side(int side) => Units.Where(u => u.Side == side);
        public BattleUnit Unit(int side, int index) => Units.First(u => u.Side == side && u.Index == index);
    }

    public sealed class BattleRuleException : Exception
    {
        public string Code { get; }
        public BattleRuleException(string code, string message) : base(message) { Code = code; }
    }

    /// <summary>Server-authoritative simultaneous-turn battle resolver. Deterministic for a given state + actions.</summary>
    public sealed class BattleEngine
    {
        private readonly GameCatalog _cat;
        public BattleEngine(GameCatalog cat) { _cat = cat; }

        public static BattleUnit CreateUnit(GameCatalog cat, int side, int index, int position, long cardId,
            string formId, int level, string rarity, string quality, int skillLevel = 1)
        {
            var f = cat.Form(formId);
            var s = StatCalculator.Compute(cat, formId, level, rarity, quality);
            var u = new BattleUnit
            {
                Side = side, Index = index, Position = position, CardId = cardId, FormId = formId,
                Element = f.Element, Level = level, Rarity = rarity, MaxHp = s.Hp, Hp = s.Hp, Atk = s.Atk, Def = s.Def,
                Spd = s.Spd, Crit = s.Crit, SkillId = f.Skill, UltimateId = f.Ultimate, PassiveId = f.Passive,
                SkillLevel = Math.Max(1, Math.Min(5, skillLevel)),
            };
            // permanent stat passives are folded in at creation
            var p = u.PassiveId != null ? cat.Passives[u.PassiveId] : null;
            if (p != null && p.Type == "stat_bonus")
            {
                switch (p.Stat)
                {
                    case "atk": u.Atk = (int)Math.Round(u.Atk * (1 + p.Pct)); break;
                    case "def": u.Def = (int)Math.Round(u.Def * (1 + p.Pct)); break;
                    case "spd": u.Spd = (int)Math.Round(u.Spd * (1 + p.Pct)); break;
                    case "crit": u.Crit += p.Pct; break;
                }
            }
            return u;
        }

        public BattleState Start(List<BattleUnit> sideA, List<BattleUnit> sideB, ulong seed, BattleConfig? config = null)
        {
            var st = new BattleState { RngState = seed, Config = config ?? new BattleConfig() };
            st.Units.AddRange(sideA);
            st.Units.AddRange(sideB);
            ApplySynergies(st, 0, st.SynergiesA);
            ApplySynergies(st, 1, st.SynergiesB);
            for (int side = 0; side < 2; side++)
            {
                int bonus = st.Side(side).Where(u => u.PassiveId != null)
                    .Select(u => _cat.Passives[u.PassiveId!]).Where(p => p.Type == "battle_start_energy").Sum(p => p.Amount);
                st.Energy[side] = Math.Min(st.Config.MaxEnergy, st.Config.StartEnergy + bonus);
            }
            return st;
        }

        /// <summary>Team synergies (configurable, applied once at battle start, server-side).</summary>
        public void ApplySynergies(BattleState st, int side, List<string> applied)
        {
            var team = st.Side(side).ToList();
            var cfg = st.Config;
            foreach (var g in team.GroupBy(u => u.Element).Where(g => g.Count() >= 3))
            {
                foreach (var u in g) u.Atk = (int)Math.Round(u.Atk * (1 + cfg.ElementSynergyAtk));
                applied.Add("element_" + g.Key);
            }
            if (team.Select(u => u.Element).Distinct().Count() >= 4)
            {
                foreach (var u in team) { u.MaxHp = (int)Math.Round(u.MaxHp * (1 + cfg.HarmonySynergyHp)); u.Hp = u.MaxHp; }
                applied.Add("harmony");
            }
            bool guardFront = team.Any(u => u.IsFront && _cat.Forms.TryGetValue(u.FormId, out var f) && f.Role == "guardian");
            bool supportBack = team.Any(u => !u.IsFront && _cat.Forms.TryGetValue(u.FormId, out var f) && f.Role == "support");
            if (guardFront && supportBack)
            {
                foreach (var u in team) u.Def = (int)Math.Round(u.Def * (1 + cfg.FormationSynergyDef));
                applied.Add("formation");
            }
        }

        public int Cost(BattleUnit u, ActionType t)
        {
            switch (t)
            {
                case ActionType.Skill: return u.SkillId != null ? _cat.Skills[u.SkillId].Cost : int.MaxValue;
                case ActionType.Ultimate: return u.UltimateId != null ? _cat.Skills[u.UltimateId].Cost : int.MaxValue;
                default: return 0;
            }
        }

        /// <summary>Validates and normalises one side's plan. Alive, non-stunned units without an action get a
        /// default Basic attack. Throws BattleRuleException for illegal plans.</summary>
        public List<PlannedAction> Normalize(BattleState st, int side, IList<PlannedAction>? plan)
        {
            plan = plan ?? new List<PlannedAction>();
            var result = new List<PlannedAction>();
            var seen = new HashSet<int>();
            int energy = 0;
            foreach (var a in plan)
            {
                if (a.UnitIndex < 0 || a.UnitIndex > 4) throw new BattleRuleException("invalid_unit", "Unit index out of range");
                if (!seen.Add(a.UnitIndex)) throw new BattleRuleException("duplicate_action", "Unit acts twice");
                var u = st.Unit(side, a.UnitIndex);
                if (!u.Alive) throw new BattleRuleException("unit_defeated", "Defeated unit cannot act");
                if (u.Has("stun")) throw new BattleRuleException("unit_stunned", "Stunned unit cannot act");
                if (!Enum.IsDefined(typeof(ActionType), a.Type)) throw new BattleRuleException("invalid_action", "Unknown action");
                int c = Cost(u, a.Type);
                if (c == int.MaxValue) throw new BattleRuleException("no_such_skill", "Unit has no such skill");
                energy += c;
                if (a.TargetIndex.HasValue && (a.TargetIndex < 0 || a.TargetIndex > 4))
                    throw new BattleRuleException("invalid_target", "Target index out of range");
                result.Add(new PlannedAction { UnitIndex = a.UnitIndex, Type = a.Type, TargetIndex = a.TargetIndex });
            }
            if (energy > st.Energy[side]) throw new BattleRuleException("not_enough_energy", "Plan exceeds available energy");
            foreach (var u in st.Side(side).Where(x => x.Alive && !x.Has("stun") && !seen.Contains(x.Index)))
                result.Add(new PlannedAction { UnitIndex = u.Index, Type = ActionType.Basic });
            return result;
        }

        public List<BattleEvent> ResolveRound(BattleState st, IList<PlannedAction>? planA, IList<PlannedAction>? planB)
        {
            if (st.Finished) throw new BattleRuleException("battle_finished", "Battle already finished");
            var a = Normalize(st, 0, planA);
            var b = Normalize(st, 1, planB);
            var rng = new SeededRandom(st.RngState);
            var events = new List<BattleEvent>();
            var cfg = st.Config;

            var ordered = new List<(int side, PlannedAction act, int prio, int spd, int tie)>();
            foreach (var x in a) ordered.Add((0, x, Priority(st.Unit(0, x.UnitIndex), x.Type), st.Unit(0, x.UnitIndex).Spd, rng.Next(1 << 30)));
            foreach (var x in b) ordered.Add((1, x, Priority(st.Unit(1, x.UnitIndex), x.Type), st.Unit(1, x.UnitIndex).Spd, rng.Next(1 << 30)));
            // Priority desc, then SPD desc, then a seeded coin flip (documented speed-tie rule)
            ordered.Sort((p, q) => p.prio != q.prio ? q.prio.CompareTo(p.prio) : p.spd != q.spd ? q.spd.CompareTo(p.spd) : p.tie.CompareTo(q.tie));

            foreach (var o in ordered)
            {
                if (st.Finished) break;
                var actor = st.Unit(o.side, o.act.UnitIndex);
                if (!actor.Alive) continue;
                if (actor.Has("stun")) { events.Add(Ev(st, "skipped_stunned", actor)); continue; }
                int cost = Cost(actor, o.act.Type);
                if (cost > st.Energy[o.side]) { events.Add(Ev(st, "skipped_energy", actor)); continue; }
                st.Energy[o.side] -= cost;
                Execute(st, actor, o.act, rng, events);
                CheckVictory(st, events);
            }

            if (!st.Finished) EndOfRound(st, rng, events);
            st.RngState = rng.State;
            return events;
        }

        private int Priority(BattleUnit u, ActionType t)
        {
            if (t == ActionType.Defend) return 0 + 3;
            if (t == ActionType.Skill && u.SkillId != null) return _cat.Skills[u.SkillId].Priority;
            if (t == ActionType.Ultimate && u.UltimateId != null) return _cat.Skills[u.UltimateId].Priority;
            return 0;
        }

        private static BattleEvent Ev(BattleState st, string type, BattleUnit actor, BattleUnit? target = null) =>
            new BattleEvent
            {
                Type = type, Round = st.Round, ActorSide = actor.Side, ActorIndex = actor.Index,
                TargetSide = target?.Side ?? -1, TargetIndex = target?.Index ?? -1,
            };

        private void Execute(BattleState st, BattleUnit actor, PlannedAction act, SeededRandom rng, List<BattleEvent> events)
        {
            if (act.Type == ActionType.Defend)
            {
                actor.Defending = true;
                events.Add(Ev(st, "defend", actor));
                return;
            }
            SkillDef skill;
            if (act.Type == ActionType.Basic)
                skill = BasicAttack;
            else
                skill = _cat.Skills[act.Type == ActionType.Skill ? actor.SkillId! : actor.UltimateId!];

            var start = Ev(st, "action", actor);
            start.SkillId = act.Type == ActionType.Basic ? "basic_attack" : skill.Id;
            events.Add(start);

            foreach (var eff in skill.Effects)
            {
                var targets = ResolveTargets(st, actor, eff.TargetOverride ?? skill.Target, act.TargetIndex);
                foreach (var t in targets)
                {
                    if (!t.Alive && eff.Type != "revive") continue;
                    ApplyEffect(st, actor, t, eff, skill, rng, events);
                    if (st.Finished) return;
                }
            }
        }

        private static readonly SkillDef BasicAttack = new SkillDef
        {
            Id = "basic_attack", Kind = "basic", Cost = 0, Target = "enemy_single",
            Effects = new List<SkillEffect> { new SkillEffect { Type = "damage", Power = 1.0 } },
        };

        /// <summary>Legal single-target enemies: taunting units first, else frontline while any frontline lives.</summary>
        public static List<BattleUnit> LegalEnemyTargets(BattleState st, int attackerSide)
        {
            var enemies = st.Side(1 - attackerSide).Where(u => u.Alive).ToList();
            var taunt = enemies.Where(u => u.Has("taunt")).ToList();
            if (taunt.Count > 0) return taunt;
            var front = enemies.Where(u => u.IsFront).ToList();
            return front.Count > 0 ? front : enemies;
        }

        private List<BattleUnit> ResolveTargets(BattleState st, BattleUnit actor, string target, int? chosen)
        {
            var allies = st.Side(actor.Side).Where(u => u.Alive).ToList();
            var enemies = st.Side(1 - actor.Side).Where(u => u.Alive).ToList();
            switch (target)
            {
                case "self": return new List<BattleUnit> { actor };
                case "ally_all": return allies;
                case "ally_lowest_hp":
                    return allies.OrderBy(u => (double)u.Hp / u.MaxHp).ThenBy(u => u.Index).Take(1).ToList();
                case "ally_single":
                {
                    var pick = chosen.HasValue ? allies.FirstOrDefault(u => u.Index == chosen.Value) : null;
                    return new List<BattleUnit> { pick ?? allies.OrderBy(u => (double)u.Hp / u.MaxHp).First() };
                }
                case "enemy_all": return enemies;
                case "enemy_front_row":
                {
                    var front = enemies.Where(u => u.IsFront).ToList();
                    return front.Count > 0 ? front : enemies;
                }
                default: // enemy_single
                {
                    var legal = LegalEnemyTargets(st, actor.Side);
                    if (legal.Count == 0) return legal;
                    var pick = chosen.HasValue ? legal.FirstOrDefault(u => u.Index == chosen.Value) : null;
                    // invalid / defeated choice: retarget deterministically to the lowest-index legal target
                    return new List<BattleUnit> { pick ?? legal.OrderBy(u => u.Index).First() };
                }
            }
        }

        private PassiveDef? Passive(BattleUnit u) => u.PassiveId != null ? _cat.Passives[u.PassiveId] : null;

        private int EffectiveAtk(BattleState st, BattleUnit u)
        {
            double atk = u.Atk;
            if (u.Has("weaken")) atk *= st.Config.WeakenAtk;
            var p = Passive(u);
            if (p != null && p.Type == "low_hp_stat_bonus" && p.Stat == "atk" && u.Hp <= u.MaxHp * p.Threshold)
                atk *= 1 + p.Pct;
            return (int)Math.Round(atk);
        }

        private void ApplyEffect(BattleState st, BattleUnit actor, BattleUnit t, SkillEffect eff, SkillDef skill,
            SeededRandom rng, List<BattleEvent> events)
        {
            var cfg = st.Config;
            double lvl = skill.Kind == "basic" ? 1.0 : 1.0 + cfg.SkillLevelBonus * (actor.SkillLevel - 1);
            switch (eff.Type)
            {
                case "damage":
                {
                    double raw = eff.Power * lvl * EffectiveAtk(st, actor);
                    double mitig = cfg.DefenseConstant / (cfg.DefenseConstant + t.Def);
                    double elem = _cat.Elements.Multiplier(actor.Element, t.Element);
                    bool crit = rng.Chance(actor.Crit);
                    double dmg = raw * mitig * elem * (crit ? cfg.CritMultiplier : 1.0);
                    if (t.Defending) dmg *= cfg.DefendMultiplier;
                    var tp = Passive(t);
                    if (tp != null && tp.Type == "damage_reduction") dmg *= 1 - tp.Pct;
                    int total = Math.Max(1, (int)Math.Round(dmg));
                    int absorbed = Math.Min(t.Shield, total);
                    t.Shield -= absorbed;
                    int hpLoss = Math.Min(t.Hp, total - absorbed);
                    t.Hp -= hpLoss;
                    var e = Ev(st, "damage", actor, t);
                    e.Amount = total; e.Crit = crit; e.ElementMultiplier = elem; e.SkillId = skill.Id;
                    events.Add(e);
                    if (absorbed > 0) { var s = Ev(st, "shield_absorb", actor, t); s.Amount = absorbed; events.Add(s); }
                    var ap = Passive(actor);
                    if (ap != null && ap.Type == "lifesteal" && hpLoss > 0 && actor.Alive)
                        Heal(st, actor, actor, (int)Math.Round(hpLoss * ap.Pct), events);
                    if (!t.Alive) Defeat(st, t, events);
                    break;
                }
                case "heal":
                {
                    double amt = eff.Power * lvl * EffectiveAtk(st, actor);
                    var ap = Passive(actor);
                    if (ap != null && ap.Type == "heal_bonus") amt *= 1 + ap.Pct;
                    Heal(st, actor, t, (int)Math.Round(amt), events);
                    break;
                }
                case "shield":
                {
                    int basis = eff.Scale == "def" ? actor.Def : eff.Scale == "maxhp" ? actor.MaxHp : EffectiveAtk(st, actor);
                    int amt = (int)Math.Round(eff.Power * lvl * basis);
                    t.Shield += amt;
                    var e = Ev(st, "shield", actor, t); e.Amount = amt; events.Add(e);
                    break;
                }
                case "status":
                {
                    if (eff.Status == null) break;
                    if (!rng.Chance(eff.Chance)) break;
                    var existing = t.Statuses.FirstOrDefault(s => s.Type == eff.Status);
                    if (existing != null) { existing.Remaining = Math.Max(existing.Remaining, eff.Duration); existing.Fresh = true; }
                    else t.Statuses.Add(new StatusInstance { Type = eff.Status, Remaining = eff.Duration, Fresh = true });
                    var e = Ev(st, "status_applied", actor, t); e.Status = eff.Status; e.Amount = eff.Duration; events.Add(e);
                    break;
                }
                case "cleanse":
                {
                    int removed = t.Statuses.RemoveAll(s => s.Type == "burn" || s.Type == "poison" || s.Type == "stun" || s.Type == "weaken");
                    if (removed > 0) { var e = Ev(st, "cleanse", actor, t); e.Amount = removed; events.Add(e); }
                    break;
                }
                case "energy":
                {
                    st.Energy[actor.Side] = Math.Min(cfg.MaxEnergy, st.Energy[actor.Side] + (int)eff.Power);
                    break;
                }
            }
        }

        private static void Heal(BattleState st, BattleUnit actor, BattleUnit t, int amount, List<BattleEvent> events)
        {
            int healed = Math.Min(t.MaxHp - t.Hp, Math.Max(0, amount));
            t.Hp += healed;
            var e = Ev(st, "heal", actor, t); e.Amount = healed; events.Add(e);
        }

        private static void Defeat(BattleState st, BattleUnit t, List<BattleEvent> events)
        {
            t.Shield = 0;
            t.Statuses.Clear();
            events.Add(new BattleEvent { Type = "defeated", Round = st.Round, TargetSide = t.Side, TargetIndex = t.Index });
        }

        private void EndOfRound(BattleState st, SeededRandom rng, List<BattleEvent> events)
        {
            var cfg = st.Config;
            foreach (var u in st.Units.Where(x => x.Alive).OrderBy(x => x.Side).ThenBy(x => x.Index))
            {
                foreach (var s in u.Statuses.ToList())
                {
                    if (s.Type == "burn" || s.Type == "poison")
                    {
                        int dmg = Math.Max(1, (int)Math.Round(u.MaxHp * (s.Type == "burn" ? cfg.BurnPct : cfg.PoisonPct)));
                        dmg = Math.Min(dmg, u.Hp);
                        u.Hp -= dmg;
                        events.Add(new BattleEvent { Type = "status_tick", Round = st.Round, TargetSide = u.Side, TargetIndex = u.Index, Amount = dmg, Status = s.Type });
                        if (!u.Alive) { Defeat(st, u, events); break; }
                    }
                }
                if (!u.Alive) continue;
                var p = Passive(u);
                if (p != null && p.Type == "regen_round_end" && u.Hp < u.MaxHp)
                    Heal(st, u, u, (int)Math.Round(u.MaxHp * p.Pct), events);
            }
            foreach (var u in st.Units)
            {
                u.Defending = false;
                foreach (var s in u.Statuses)
                {
                    if (s.Fresh && s.Type == "stun") { s.Fresh = false; continue; } // stun lasts through next round
                    s.Fresh = false;
                    s.Remaining--;
                }
                u.Statuses.RemoveAll(s => s.Remaining <= 0);
            }
            CheckVictory(st, events);
            if (st.Finished) return;
            if (st.Round >= cfg.MaxRounds)
            {
                Tiebreak(st, events);
                return;
            }
            st.Round++;
            for (int side = 0; side < 2; side++)
                st.Energy[side] = Math.Min(cfg.MaxEnergy, st.Energy[side] + cfg.EnergyPerRound);
            events.Add(new BattleEvent { Type = "round_start", Round = st.Round });
        }

        private static void CheckVictory(BattleState st, List<BattleEvent> events)
        {
            bool aDead = !st.Side(0).Any(u => u.Alive);
            bool bDead = !st.Side(1).Any(u => u.Alive);
            if (!aDead && !bDead) return;
            Finish(st, aDead && bDead ? -1 : aDead ? 1 : 0, "knockout", events);
        }

        /// <summary>Round limit tiebreak: more surviving cards, then higher total remaining HP %, else draw.</summary>
        private static void Tiebreak(BattleState st, List<BattleEvent> events)
        {
            int aliveA = st.Side(0).Count(u => u.Alive), aliveB = st.Side(1).Count(u => u.Alive);
            if (aliveA != aliveB) { Finish(st, aliveA > aliveB ? 0 : 1, "round_limit_survivors", events); return; }
            double hpA = st.Side(0).Sum(u => (double)u.Hp / u.MaxHp), hpB = st.Side(1).Sum(u => (double)u.Hp / u.MaxHp);
            if (Math.Abs(hpA - hpB) > 1e-9) { Finish(st, hpA > hpB ? 0 : 1, "round_limit_hp", events); return; }
            Finish(st, -1, "round_limit_draw", events);
        }

        public static void Forfeit(BattleState st, int losingSide, string reason, List<BattleEvent> events)
        {
            if (st.Finished) return;
            Finish(st, 1 - losingSide, reason, events);
        }

        private static void Finish(BattleState st, int winner, string reason, List<BattleEvent> events)
        {
            st.Finished = true;
            st.Winner = winner;
            st.EndReason = reason;
            events.Add(new BattleEvent { Type = "battle_end", Round = st.Round, ActorSide = winner, Status = reason });
        }
    }

    /// <summary>Rule-abiding PvE AI. Uses only legal actions within its own energy (no resource cheating).</summary>
    public sealed class BattleAi
    {
        private readonly GameCatalog _cat;
        private readonly BattleEngine _engine;
        public BattleAi(GameCatalog cat) { _cat = cat; _engine = new BattleEngine(cat); }

        public List<PlannedAction> Plan(BattleState st, int side, int difficulty)
        {
            var plan = new List<PlannedAction>();
            int energy = st.Energy[side];
            var units = st.Side(side).Where(u => u.Alive && !u.Has("stun")).OrderByDescending(u => u.Atk).ToList();
            var targets = BattleEngine.LegalEnemyTargets(st, side);
            if (targets.Count == 0) return plan;
            var allies = st.Side(side).Where(u => u.Alive).ToList();
            bool allyHurt = allies.Any(u => u.Hp < u.MaxHp * 0.6);

            foreach (var u in units)
            {
                var act = new PlannedAction { UnitIndex = u.Index, Type = ActionType.Basic };
                // focus the legal target with best element advantage, then lowest HP
                var best = targets.OrderByDescending(t => _cat.Elements.Multiplier(u.Element, t.Element))
                                  .ThenBy(t => t.Hp).First();
                act.TargetIndex = best.Index;
                if (difficulty >= 2 && u.UltimateId != null && _engine.Cost(u, ActionType.Ultimate) <= energy && Useful(u.UltimateId, allyHurt))
                    act.Type = ActionType.Ultimate;
                else if (difficulty >= 1 && u.SkillId != null && _engine.Cost(u, ActionType.Skill) <= energy && Useful(u.SkillId, allyHurt))
                    act.Type = ActionType.Skill;
                energy -= _engine.Cost(u, act.Type);
                plan.Add(act);
            }
            return plan;
        }

        private bool Useful(string skillId, bool allyHurt)
        {
            var s = _cat.Skills[skillId];
            bool healing = s.Effects.All(e => e.Type == "heal" || e.Type == "cleanse");
            return !healing || allyHurt;
        }
    }
}
