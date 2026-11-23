// Cross-engine check: the TypeScript Battle 3v3 engine must reproduce battles played by the authoritative C# engine.
import { describe, expect, it } from 'vitest';
import fixtures from './__fixtures__/team-fixtures.json';
import { apply, create, decide, mustAct, timeout } from './engine';
import { digest, type TeamAction, type TeamRules, type TeamSetupPlayer } from './model';

interface Case { seed: string; aiOnly: boolean; teams: TeamSetupPlayer[]; steps: { p: number; a: TeamAction | null }[]; winner: number | null; endReason: string; round: number; log: string[] }
async function loadRules(): Promise<TeamRules> {
  const fsName = 'node:fs';
  const { readFileSync } = await import(/* @vite-ignore */ fsName) as { readFileSync: (f: string, e: string) => string };
  return JSON.parse(readFileSync('public/data/team_battle.json', 'utf8')) as TeamRules;
}
const cases = (fixtures as unknown as { version: string; cases: Case[] }).cases;

describe('battle 3v3: TypeScript engine = C# engine', () => {
  it('ships the battle data version the fixtures were made with', async () => {
    const rules = await loadRules();
    expect(rules.version).toBe((fixtures as { version: string }).version);
    expect(Object.keys(rules.kits)).toHaveLength(215);
  });
  it.each(cases.map((c, i) => [i, c] as const))('replays case %i plan by plan to the identical log', async (_i, c) => {
    const rules = await loadRules();
    const st = create(rules, c.teams[0], c.teams[1], BigInt(c.seed));
    for (const s of c.steps) { if (s.p < 0) timeout(rules, st); else apply(rules, st, s.p, s.a!); }
    expect(st.log.map(digest)).toEqual(c.log);
    expect(st.winner).toBe(c.winner);
  });
  it.each(cases.filter((c) => c.aiOnly).map((c, i) => [i, c] as const))('the TypeScript AI picks the same plans (case %i)', async (_i, c) => {
    const rules = await loadRules();
    const st = create(rules, c.teams[0], c.teams[1], BigInt(c.seed));
    for (let n = 0; st.phase !== 'finished' && n < 400; n++) { const p = mustAct(st, 0) ? 0 : 1; apply(rules, st, p, decide(rules, st, p)); }
    expect(st.log.map(digest)).toEqual(c.log);
  });
});
