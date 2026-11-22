using System;
using System.Collections.Generic;

// Pet Card Tactical Duel — data model. Shared by the server (authoritative) and the Unity client (display only).
// Every number is an integer so the TypeScript port used by the Interactive Web Preview produces identical results
// for the same seed and actions (checked by cross-engine fixtures).
namespace PetStar.Rules.Duel
{
    public sealed class DuelConfig
    {
        public int DeckSize { get; set; } = 40;
        public int PetMin { get; set; } = 10;
        public int PetMax { get; set; } = 25;
        public int MaxCopies { get; set; } = 3;
        public int DivineMaxCopies { get; set; } = 1;
        public int StartLp { get; set; } = 2500;
        public int DeathPenalty { get; set; } = 500;
        public int StartHand { get; set; } = 5;
        public int MaxHand { get; set; } = 7;
        public int PetZones { get; set; } = 5;
        public int FrontZones { get; set; } = 3;
        public int SpellTrapZones { get; set; } = 3;
        public int MaxEnergy { get; set; } = 8;
        public int StartEnergy { get; set; } = 2;
        public int EnergyPerTurn { get; set; } = 1;
        public int MaxTurnsPerPlayer { get; set; } = 40;
        public int TurnSeconds { get; set; } = 60;
        public int ReactionSeconds { get; set; } = 15;
        public int ChainMaxDepth { get; set; } = 8;
        /// <summary>Basic damage multiplier in percent (300 = ×3).</summary>
        public int DamageScale { get; set; } = 300;
        public int CritPct { get; set; } = 150;
        public int StrongPct { get; set; } = 150;
        public int WeakPct { get; set; } = 75;
        public int GuardPct { get; set; } = 50;
        public int WeakenPct { get; set; } = 75;
        public int BurnPct { get; set; } = 6;
        public int PoisonPct { get; set; } = 8;
        /// <summary>Energy to evolve from stage index (1→2 uses [1], 2→3 uses [2]).</summary>
        public List<int> EvolveCost { get; set; } = new List<int> { 0, 3, 5 };
        public DuelTributes Tributes { get; set; } = new DuelTributes();
        /// <summary>Ranked caps the out-of-duel growth multiplier (stars × enhancement × awakening) at this percent.</summary>
        public int RankedGrowthCap { get; set; } = 145;
        public int TimeoutsToForfeit { get; set; } = 3;
    }

    public sealed class DuelTributes
    {
        public int Stage1 { get; set; }
        public int Stage2 { get; set; } = 1;
        public int Stage3 { get; set; } = 2;
        public int Divine { get; set; } = 2;
    }

    public sealed class TacticEffect
    {
        public string Op { get; set; } = "";
        public string? Target { get; set; }
        public int Pct { get; set; }
        public string? Stat { get; set; }
        public int Turns { get; set; }
        public string? Status { get; set; }
        public int Chance { get; set; } = 100;
        public string? Element { get; set; }
        public int N { get; set; }
        public int MaxStage { get; set; }
        public string? What { get; set; }
    }

    /// <summary>Spell or Trap card definition (database/seed/duel_cards.json).</summary>
    public sealed class TacticDef
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "spell";       // spell | trap
        public string Speed { get; set; } = "normal";     // normal | quick | trap
        public string Category { get; set; } = "";
        public int Cost { get; set; }
        public string Targeting { get; set; } = "none";   // none | own_pet | enemy_pet | own_two | hand_pet | grave_pet | enemy_st
        public string? Trigger { get; set; }              // traps: attack | summon | spell | trap | destroyed
        public List<TacticEffect> Effects { get; set; } = new List<TacticEffect>();
        public bool OncePerTurn { get; set; }
        public bool OncePerDuel { get; set; }
        public Dictionary<string, string> Name { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> Text { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>Server-computed stats of one form of a deck Pet (current form first, then its evolution line).</summary>
    public sealed class PetFormStats
    {
        public string FormId { get; set; } = "";
        public int Stage { get; set; } = 1;
        public bool Divine { get; set; }
        public string Element { get; set; } = "";
        public string Role { get; set; } = "";
        public int Hp { get; set; }
        public int Atk { get; set; }
        public int Def { get; set; }
        public int Spd { get; set; }
        /// <summary>Critical chance in basis points (800 = 8 %).</summary>
        public int CritBp { get; set; }
        public string? Skill { get; set; }
        public string? Ultimate { get; set; }
        public string? Passive { get; set; }
    }

    public sealed class DuelCard
    {
        public int Id { get; set; }
        public string Kind { get; set; } = "pet";         // pet | tactic
        public string? TacticId { get; set; }
        public long CardInstanceId { get; set; }
        public List<PetFormStats> Forms { get; set; } = new List<PetFormStats>();
    }

    public sealed class DuelSetupPlayer
    {
        public string Name { get; set; } = "";
        public List<DuelCard> Deck { get; set; } = new List<DuelCard>();
    }

    /// <summary>Skill / passive data the duel needs (from skills.json), in duel-friendly integer form.</summary>
    public sealed class DuelSkillEffect
    {
        public string Type { get; set; } = "";            // damage | heal | shield | status | cleanse | energy
        public int PowerPct { get; set; }
        public string Scale { get; set; } = "atk";
        public string? Status { get; set; }
        public int Chance { get; set; } = 100;
        public int Duration { get; set; }
    }

    public sealed class DuelSkill
    {
        public string Id { get; set; } = "";
        public int Cost { get; set; }
        public string Target { get; set; } = "enemy_single";
        public List<DuelSkillEffect> Effects { get; set; } = new List<DuelSkillEffect>();
    }

    public sealed class DuelPassive
    {
        public string Type { get; set; } = "";
        public string? Stat { get; set; }
        public int Pct { get; set; }
        public int Amount { get; set; }
    }

    public sealed class DuelRules
    {
        public DuelConfig Config { get; set; } = new DuelConfig();
        public Dictionary<string, TacticDef> Tactics { get; set; } = new Dictionary<string, TacticDef>();
        public Dictionary<string, DuelSkill> Skills { get; set; } = new Dictionary<string, DuelSkill>();
        public Dictionary<string, DuelPassive> Passives { get; set; } = new Dictionary<string, DuelPassive>();
        /// <summary>element → elements it is strong against.</summary>
        public Dictionary<string, List<string>> Strong { get; set; } = new Dictionary<string, List<string>>();
    }

    // ------------------------------------------------------------------ state

    public sealed class DuelStatus
    {
        public string Type { get; set; } = "";
        public int Remaining { get; set; }
    }

    public sealed class DuelBuff
    {
        public string Stat { get; set; } = "atk";
        public int Pct { get; set; }
        /// <summary>Expires at the end of this global turn.</summary>
        public int UntilTurn { get; set; }
    }

    public sealed class DuelUnit
    {
        public int Uid { get; set; }
        public int CardId { get; set; }
        public int Owner { get; set; }
        public int Zone { get; set; }
        public int FormIndex { get; set; }
        public string FormId { get; set; } = "";
        public int Stage { get; set; }
        public bool Divine { get; set; }
        public string Element { get; set; } = "";
        public string Role { get; set; } = "";
        public int MaxHp { get; set; }
        public int Hp { get; set; }
        public int Atk { get; set; }
        public int Def { get; set; }
        public int Spd { get; set; }
        public int CritBp { get; set; }
        public int Shield { get; set; }
        public string? Skill { get; set; }
        public string? Ultimate { get; set; }
        public string? Passive { get; set; }
        public int SummonedTurn { get; set; }
        public bool Acted { get; set; }
        public bool Moved { get; set; }
        public int EvolvedTurn { get; set; }
        public List<DuelStatus> Statuses { get; set; } = new List<DuelStatus>();
        public List<DuelBuff> Buffs { get; set; } = new List<DuelBuff>();
    }

    public sealed class SetCard
    {
        public int CardId { get; set; }
        public string TacticId { get; set; } = "";
        public int SetTurn { get; set; }
    }

    public sealed class DuelPlayerState
    {
        public string Name { get; set; } = "";
        public int Lp { get; set; }
        public List<int> Deck { get; set; } = new List<int>();
        public List<int> Hand { get; set; } = new List<int>();
        public List<int> Grave { get; set; } = new List<int>();
        public List<DuelUnit> Units { get; set; } = new List<DuelUnit>();
        /// <summary>Spell/Trap zones; null = empty.</summary>
        public List<SetCard?> Zones { get; set; } = new List<SetCard?>();
        public int Energy { get; set; }
        public int MaxEnergy { get; set; }
        public bool NormalSummonUsed { get; set; }
        public int TurnsTaken { get; set; }
        public List<string> UsedThisTurn { get; set; } = new List<string>();
        public List<string> UsedThisDuel { get; set; } = new List<string>();
        public int Timeouts { get; set; }
    }

    public sealed class ChainLink
    {
        public int Player { get; set; }
        public string Kind { get; set; } = "";            // spell | trap | attack | skill | ultimate
        public int CardId { get; set; }
        public string? TacticId { get; set; }
        public List<int> Targets { get; set; } = new List<int>();
        public int Attacker { get; set; }
        public int Target { get; set; }
        public bool Negated { get; set; }
    }

    /// <summary>Public log line. Digest = "type:p:a:b:n:s".</summary>
    public sealed class DuelEvent
    {
        public string Type { get; set; } = "";
        public int P { get; set; } = -1;
        public int A { get; set; }
        public int B { get; set; }
        public int N { get; set; }
        public string S { get; set; } = "";
        public string Digest() => Type + ":" + P + ":" + A + ":" + B + ":" + N + ":" + S;
    }

    public sealed class DuelState
    {
        public ulong Rng { get; set; }
        public List<DuelCard> Cards { get; set; } = new List<DuelCard>();
        public List<DuelPlayerState> Players { get; set; } = new List<DuelPlayerState>();
        /// <summary>Global turn number (1, 2, 3 ...).</summary>
        public int Turn { get; set; }
        public int Active { get; set; }
        public int First { get; set; }
        public string Phase { get; set; } = "main";        // main | battle | finished
        public List<ChainLink> Chain { get; set; } = new List<ChainLink>();
        /// <summary>Player who may respond now (-1 = no reaction window open).</summary>
        public int Priority { get; set; } = -1;
        public bool LastPassed { get; set; }
        /// <summary>Open trigger window without chain: summon | destroyed (with its subject).</summary>
        public string? Event { get; set; }
        public int EventPlayer { get; set; } = -1;
        public int EventUnit { get; set; }
        public int? Winner { get; set; }
        public string? EndReason { get; set; }
        public int NextUid { get; set; } = 1;
        public int Version { get; set; }
        public List<DuelEvent> Log { get; set; } = new List<DuelEvent>();

        public DuelCard Card(int id) => Cards[id - 1];
        public bool Finished => Phase == "finished";
        /// <summary>Who has to act now: the player holding priority in a reaction window, otherwise the active player.</summary>
        public int ToAct => Priority >= 0 ? Priority : Active;
    }

    public sealed class DuelAction
    {
        public string Type { get; set; } = "";            // summon | spell | set | activate | evolve | move | battle | attack | skill | ultimate | pass | end | surrender
        public int Card { get; set; }
        public int Zone { get; set; } = -1;
        public int Unit { get; set; }
        public int Target { get; set; }
        public List<int> Targets { get; set; } = new List<int>();
        public List<int> Tributes { get; set; } = new List<int>();
    }

    public sealed class DuelRuleException : Exception
    {
        public string Code { get; }
        public DuelRuleException(string code) : base(code) { Code = code; }
    }
}
