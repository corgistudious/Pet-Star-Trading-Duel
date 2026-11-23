using System;
using System.Collections.Generic;

// Pet Star Battle 3v3: data model. Original turn-based engine: two teams of 3 Pets, both players lock orders for
// every Pet at the same time, the server resolves the round by skill priority then SPD. Shared by the server
// (authoritative), the Unity client (display/hints) and, via a TypeScript port checked by cross-engine fixtures,
// the web preview. Integer math only; randomness is a seeded SplitMix64 stored in the state (exact replays).
namespace PetStar.Rules.Team
{
    public sealed class TeamConfig
    {
        public int TeamSize { get; set; } = 3;
        public int StartEn { get; set; } = 50;
        public int MaxEn { get; set; } = 100;
        public int BasicEnGain { get; set; } = 15;
        public int RoundEnGain { get; set; } = 10;
        public int MaxRounds { get; set; } = 30;
        public int DamageScalePct { get; set; } = 160;
        public int CritDamagePct { get; set; } = 150;
        public int StrongPct { get; set; } = 150;
        public int WeakPct { get; set; } = 75;
        public int GuardReductionPct { get; set; } = 50;
        public int BurnPct { get; set; } = 6;
        public int PoisonPct { get; set; } = 5;
        public int PoisonMaxStacks { get; set; } = 3;
        public int RegenPct { get; set; } = 6;
        public int HastePct { get; set; } = 30;
        public int WeakenPct { get; set; } = 25;
        public int CounterPct { get; set; } = 20;
        public int ReducePassivePct { get; set; } = 10;
        public int DrainPassivePct { get; set; } = 10;
        public int RegenPassivePct { get; set; } = 4;
        public int StartEnPassive { get; set; } = 20;
        public int HardCcTurns { get; set; } = 1;
        public int CcImmunityTurns { get; set; } = 1;
        public int ShieldCapPct { get; set; } = 60;
        public int PlanSeconds { get; set; } = 30;
        public int TimeoutsToForfeit { get; set; } = 3;
    }

    public sealed class TeamEffect
    {
        public string Op { get; set; } = "";          // damage heal shield status cleanse energy
        public int Power { get; set; }                // percent
        public string? Scale { get; set; }            // shield: def | res
        public string? Status { get; set; }
        public int Chance { get; set; } = 100;        // percent
        public int Turns { get; set; } = 1;
        public string? On { get; set; }               // status target: enemy | ally | self
        public int Amount { get; set; }
    }

    public sealed class TeamSkill
    {
        public string Id { get; set; } = "";
        public Dictionary<string, string> Name { get; set; } = new Dictionary<string, string>();
        public string Kind { get; set; } = "basic";   // basic | skill | ultimate
        public int Cost { get; set; }
        public int Cooldown { get; set; }
        public int Priority { get; set; }
        public string Target { get; set; } = "enemy_single";   // enemy_single enemy_all ally_all ally_lowest self
        public string Dmg { get; set; } = "phys";     // phys (ATK vs DEF) | mag (MAG vs RES)
        public List<TeamEffect> Effects { get; set; } = new List<TeamEffect>();
    }

    public sealed class TeamPassive
    {
        public string Id { get; set; } = "";
        public string Op { get; set; } = "";          // regen start_en drain reduce rage heal_bonus
        public int Pct { get; set; }
        public int Amount { get; set; }
        public int ThresholdPct { get; set; }
    }

    public sealed class TeamKit
    {
        public string Basic { get; set; } = "basic_phys";
        public string? Skill { get; set; }
        public string? Ultimate { get; set; }
        public TeamPassive? Passive { get; set; }
        public string Element { get; set; } = "";
        public string Role { get; set; } = "";
    }

    public sealed class TeamRole { public int MagPct { get; set; } = 100; public int ResPct { get; set; } = 100; public string Dmg { get; set; } = "phys"; }

    public sealed class TeamRules
    {
        public string Version { get; set; } = "";
        public TeamConfig Config { get; set; } = new TeamConfig();
        public Dictionary<string, TeamRole> Roles { get; set; } = new Dictionary<string, TeamRole>();
        public Dictionary<string, List<string>> Strong { get; set; } = new Dictionary<string, List<string>>();
        public Dictionary<string, TeamSkill> Skills { get; set; } = new Dictionary<string, TeamSkill>();
        public Dictionary<string, TeamKit> Kits { get; set; } = new Dictionary<string, TeamKit>();
    }

    // ---------------------------------------------------------------- match state

    public sealed class TeamStatus
    {
        public string Type { get; set; } = "";
        public int Remaining { get; set; }
        public int Stacks { get; set; } = 1;
    }

    public sealed class TeamUnit
    {
        public int Uid { get; set; }
        public int Side { get; set; }
        public int Slot { get; set; }
        public string FormId { get; set; } = "";
        public long CardInstanceId { get; set; }
        public string Element { get; set; } = "";
        public string Role { get; set; } = "";
        public int MaxHp { get; set; }
        public int Hp { get; set; }
        public int Atk { get; set; }
        public int Def { get; set; }
        public int Mag { get; set; }
        public int Res { get; set; }
        public int Spd { get; set; }
        public int CritBp { get; set; }
        public int En { get; set; }
        public int Shield { get; set; }
        public bool Acted { get; set; }
        public int CcImmune { get; set; }
        public string Basic { get; set; } = "basic_phys";
        public string? Skill { get; set; }
        public string? Ultimate { get; set; }
        public TeamPassive? Passive { get; set; }
        public Dictionary<string, int> Cooldowns { get; set; } = new Dictionary<string, int>();
        public List<TeamStatus> Statuses { get; set; } = new List<TeamStatus>();
        public bool Alive => Hp > 0;
    }

    /// <summary>One Pet's order for the round: slot basic | skill | ultimate | guard, and an enemy or ally uid (0 = automatic).</summary>
    public sealed class TeamOrder
    {
        public int Unit { get; set; }
        public string Slot { get; set; } = "basic";
        public int Target { get; set; }
    }

    public sealed class TeamPlayerState
    {
        public string Name { get; set; } = "";
        public List<TeamOrder>? Plan { get; set; }      // null until locked for this round; hidden from the opponent
        public bool Locked { get; set; }
        public int Timeouts { get; set; }
    }

    public sealed class TeamEvent
    {
        public string Type { get; set; } = "";
        public int Round { get; set; }
        public int A { get; set; }                     // actor uid
        public int B { get; set; }                     // target uid
        public int N { get; set; }
        public string S { get; set; } = "";
        public string Digest() => $"{Type}:{Round}:{A}:{B}:{N}:{S}";
    }

    public sealed class TeamState
    {
        public ulong Rng { get; set; }
        public string DefsVersion { get; set; } = "";
        public List<TeamUnit> Units { get; set; } = new List<TeamUnit>();
        public List<TeamPlayerState> Players { get; set; } = new List<TeamPlayerState>();
        public int Round { get; set; } = 1;
        public string Phase { get; set; } = "planning";   // planning | finished
        public int? Winner { get; set; }
        public string? EndReason { get; set; }
        public int Version { get; set; }
        public List<TeamEvent> Log { get; set; } = new List<TeamEvent>();
        public bool Finished => Phase == "finished";
    }

    public sealed class TeamAction
    {
        public string Type { get; set; } = "plan";       // plan | surrender
        public List<TeamOrder> Orders { get; set; } = new List<TeamOrder>();
    }

    /// <summary>A Pet entering battle with stats computed by the server from the owned card (never sent by clients).</summary>
    public sealed class TeamSetupUnit
    {
        public string FormId { get; set; } = "";
        public long CardInstanceId { get; set; }
        public int Hp { get; set; }
        public int Atk { get; set; }
        public int Def { get; set; }
        public int Mag { get; set; }
        public int Res { get; set; }
        public int Spd { get; set; }
        public int CritBp { get; set; }
    }

    public sealed class TeamSetupPlayer
    {
        public string Name { get; set; } = "";
        public List<TeamSetupUnit> Units { get; set; } = new List<TeamSetupUnit>();
    }

    public sealed class TeamRuleException : Exception
    {
        public string Code { get; }
        public TeamRuleException(string code) : base(code) { Code = code; }
    }
}
