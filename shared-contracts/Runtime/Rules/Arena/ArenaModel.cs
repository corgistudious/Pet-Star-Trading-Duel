using System;
using System.Collections.Generic;
using System.Linq;

// Pet Battle Arena: data model. An original Pet Star engine (no third-party battle code) for a compact card battle:
// 20-card decks, 1 Active + 3 Bench Pets, an Energy Zone outside the deck, KO points (EX = 2), 3 points to win.
// Shared by the server (authoritative), the Unity client (display/hints only) and, through a TypeScript port
// checked by cross-engine fixtures, the web preview. All numbers are integers; randomness is a seeded SplitMix64
// whose state lives inside ArenaState, so a match replays exactly from its seed and action list.
namespace PetStar.Rules.Arena
{
    public sealed class ArenaConfig
    {
        public int DeckSize { get; set; } = 20;
        public int MaxCopies { get; set; } = 2;
        public int MinBasic { get; set; } = 1;
        public int MaxEnergyTypes { get; set; } = 3;
        public int BenchSize { get; set; } = 3;
        public int HandLimit { get; set; } = 10;
        public int StartHand { get; set; } = 5;
        public int PointsToWin { get; set; } = 3;
        public int ExPoints { get; set; } = 2;
        public int NormalPoints { get; set; } = 1;
        public string SimultaneousWin { get; set; } = "draw";
        public bool FirstPlayerDraws { get; set; }
        public bool FirstPlayerEnergy { get; set; }
        public string AttackBanFirstTurn { get; set; } = "both";
        public bool NoEvolveFirstTurn { get; set; } = true;
        public bool SupporterFirstTurn { get; set; } = true;
        public int RetreatsPerTurn { get; set; } = 1;
        public int SupportersPerTurn { get; set; } = 1;
        public bool EnergyCarryOver { get; set; }
        public int WeaknessBonus { get; set; } = 20;
        public int PoisonDamage { get; set; } = 10;
        public int BurnDamage { get; set; } = 20;
        public bool BurnCureOnHeads { get; set; } = true;
        public int ConfusedSelfDamage { get; set; }
        public int MaxTurnsPerPlayer { get; set; } = 30;
        public int PveMaxTurnsPerPlayer { get; set; } = 50;
        public int TurnSeconds { get; set; } = 90;
        public int SetupSeconds { get; set; } = 60;
        public int TimeoutsToForfeit { get; set; } = 3;
        public int MaxTriggerDepth { get; set; } = 8;
        public int MaxActionsPerTurn { get; set; } = 200;
    }

    /// <summary>One data-driven effect. Ops are a closed list interpreted by ArenaEngine (no code from data, no eval).</summary>
    public sealed class ArenaEffect
    {
        public string Op { get; set; } = "";
        public string Target { get; set; } = "opp_active";
        public int Amount { get; set; }
        public string? Status { get; set; }
        public string? Cond { get; set; }
        public int Turns { get; set; }
        public string? Element { get; set; }
        public int Count { get; set; }
        public int Coins { get; set; }
    }

    public sealed class ArenaAttack
    {
        public string Id { get; set; } = "";
        public Dictionary<string, string> Name { get; set; } = new Dictionary<string, string>();
        public List<string> Cost { get; set; } = new List<string>();
        public int Damage { get; set; }
        public List<ArenaEffect> Effects { get; set; } = new List<ArenaEffect>();
        public Dictionary<string, string> Text { get; set; } = new Dictionary<string, string>();
    }

    public sealed class ArenaAbility
    {
        public string Id { get; set; } = "";
        public Dictionary<string, string> Name { get; set; } = new Dictionary<string, string>();
        public string Trigger { get; set; } = "passive";     // activated | passive | between_turns | on_attack
        public List<ArenaEffect> Effects { get; set; } = new List<ArenaEffect>();
        public Dictionary<string, string> Text { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>A battle card definition (Pet or trainer). Ownership lives elsewhere (card_instances / inventory).</summary>
    public sealed class ArenaCardDef
    {
        public string Id { get; set; } = "";
        public string Kind { get; set; } = "pet";             // pet | item | tool | supporter
        public string? Species { get; set; }
        public Dictionary<string, string> Name { get; set; } = new Dictionary<string, string>();
        public string Element { get; set; } = "";
        public string Role { get; set; } = "";
        public int Stage { get; set; }                         // 0 Basic, 1 Stage 1, 2 Stage 2
        public string? EvolvesFrom { get; set; }
        public int Hp { get; set; }
        public bool Ex { get; set; }
        public int KoPoints { get; set; } = 1;
        public string? Weakness { get; set; }
        public int Retreat { get; set; }
        public List<ArenaAttack> Attacks { get; set; } = new List<ArenaAttack>();
        public ArenaAbility? Ability { get; set; }
        public string? Target { get; set; }                    // trainers: none | own_pet | own_bench | opp_bench
        public List<ArenaEffect> Effects { get; set; } = new List<ArenaEffect>();
        public Dictionary<string, string> Text { get; set; } = new Dictionary<string, string>();
        public bool IsBasic => Kind == "pet" && Stage == 0;
    }

    public sealed class ArenaRules
    {
        public string Version { get; set; } = "";
        public ArenaConfig Config { get; set; } = new ArenaConfig();
        public List<string> Elements { get; set; } = new List<string>();
        public List<ArenaCardDef> Cards { get; set; } = new List<ArenaCardDef>();
        public Dictionary<string, int> Starter { get; set; } = new Dictionary<string, int>();
        private Dictionary<string, ArenaCardDef>? _byId;
        public ArenaCardDef Def(string id) => (_byId ??= Cards.ToDictionary(c => c.Id))[id];
        public bool Has(string id) => (_byId ??= Cards.ToDictionary(c => c.Id)).ContainsKey(id);
    }

    // ---------------------------------------------------------------- match state

    /// <summary>A timed modifier (shield, attack lock, retreat change, ...). Active while FromTurn ≤ turn ≤ UntilTurn.</summary>
    public sealed class ArenaMod
    {
        public string Kind { get; set; } = "";
        public int Amount { get; set; }
        public int FromTurn { get; set; }
        public int UntilTurn { get; set; }
        public string Source { get; set; } = "";
    }

    public sealed class ArenaPet
    {
        public int Uid { get; set; }
        public int Owner { get; set; }
        public List<int> Cards { get; set; } = new List<int>();    // evolution stack, bottom → top
        public string DefId { get; set; } = "";
        public int Damage { get; set; }
        public List<string> Energy { get; set; } = new List<string>();
        public int Tool { get; set; }
        public bool Poisoned { get; set; }
        public bool Burned { get; set; }
        public string? Special { get; set; }                      // asleep | paralyzed | confused (mutually exclusive)
        public int SpecialTurn { get; set; }
        public int PlayedTurn { get; set; }
        public int EvolvedTurn { get; set; }
        public int AbilityTurn { get; set; }
        public List<ArenaMod> Mods { get; set; } = new List<ArenaMod>();
    }

    public sealed class ArenaCard
    {
        public int Id { get; set; }
        public string DefId { get; set; } = "";
        public int Owner { get; set; }
        public long CardInstanceId { get; set; }
    }

    public sealed class ArenaPlayerState
    {
        public string Name { get; set; } = "";
        public List<int> Deck { get; set; } = new List<int>();
        public List<int> Hand { get; set; } = new List<int>();
        public List<int> Discard { get; set; } = new List<int>();
        public ArenaPet? Active { get; set; }
        public List<ArenaPet> Bench { get; set; } = new List<ArenaPet>();
        public int Points { get; set; }
        public List<string> EnergyTypes { get; set; } = new List<string>();
        public string? ZoneCurrent { get; set; }
        public string? ZoneNext { get; set; }
        public bool EnergyAttached { get; set; }
        public int SupportersUsed { get; set; }
        public int Retreats { get; set; }
        public int TurnsTaken { get; set; }
        public bool SetupDone { get; set; }
        public bool MustPromote { get; set; }
        public int Timeouts { get; set; }
        public List<ArenaMod> TurnMods { get; set; } = new List<ArenaMod>();
        public IEnumerable<ArenaPet> InPlay() { if (Active != null) yield return Active; foreach (var b in Bench) yield return b; }
    }

    public sealed class ArenaEvent
    {
        public string Type { get; set; } = "";
        public int P { get; set; } = -1;
        public int A { get; set; }
        public int B { get; set; }
        public int N { get; set; }
        public string S { get; set; } = "";
        public string Digest() => $"{Type}:{P}:{A}:{B}:{N}:{S}";
    }

    public sealed class ArenaState
    {
        public ulong Rng { get; set; }
        public string DefsVersion { get; set; } = "";
        public List<ArenaCard> Cards { get; set; } = new List<ArenaCard>();
        public List<ArenaPlayerState> Players { get; set; } = new List<ArenaPlayerState>();
        public int Turn { get; set; }
        public int Active { get; set; }
        public int First { get; set; }
        public string Phase { get; set; } = "setup";              // setup | main | finished
        public string? Continuation { get; set; }                 // checkup | next_turn (resumed after promotions)
        public int MaxTurns { get; set; } = 30;
        public int ActionsThisTurn { get; set; }
        public int? Winner { get; set; }
        public string? EndReason { get; set; }
        public int NextUid { get; set; } = 1;
        public int Version { get; set; }
        public List<ArenaEvent> Log { get; set; } = new List<ArenaEvent>();
        public ArenaCard Card(int id) => Cards[id - 1];
        public bool Finished => Phase == "finished";
    }

    public sealed class ArenaAction
    {
        public string Type { get; set; } = "";       // setup bench evolve attach item tool supporter ability retreat attack end promote surrender
        public int Card { get; set; }
        public int Uid { get; set; }
        public int Index { get; set; }
        public List<int> Targets { get; set; } = new List<int>();
        public List<int> Bench { get; set; } = new List<int>();
    }

    public sealed class ArenaSetupPlayer
    {
        public string Name { get; set; } = "";
        public List<ArenaDeckCard> Deck { get; set; } = new List<ArenaDeckCard>();
        public List<string> EnergyTypes { get; set; } = new List<string>();
    }

    public sealed class ArenaDeckCard { public string DefId { get; set; } = ""; public long CardInstanceId { get; set; } }

    public sealed class ArenaRuleException : Exception
    {
        public string Code { get; }
        public ArenaRuleException(string code) : base(code) { Code = code; }
    }
}
