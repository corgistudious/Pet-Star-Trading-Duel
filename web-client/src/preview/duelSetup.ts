// Builds the two 40-card decks for the preview duel from the sandbox collection (same deck rules as the server).
import type { DuelCard, DuelRules, PetFormStats } from '../duel/model';
import type { Catalog } from '../state/catalog';
import { stats, type SimCard, type SimState } from './sim';

const STARTER: Record<string, number> = {
  sp_herbal_tonic: 3, sp_group_remedy: 2, sp_purify: 2, sp_power_surge: 3, sp_iron_hide: 2, sp_battle_cry: 2, sp_tactical_draw: 3, sp_energy_well: 2,
  sp_formation_shift: 1, sp_scout: 2, sp_fire_storm: 1, sp_tidal_wave: 1, sp_thunderbolt: 2, sp_nature_blessing: 1, sp_call_reinforcement: 2,
  sp_second_chance: 1, sp_quick_guard: 2, sp_counter_spell: 1, sp_smoke_screen: 2, sp_dispel: 2, tr_spike_shield: 2, tr_mirror_ward: 1,
  tr_guardian_barrier: 2, tr_last_stand: 1, tr_binding_vines: 1, tr_frost_snare: 2, tr_gravity_well: 1, tr_spell_seal: 1, tr_trap_breaker: 1,
  tr_elemental_backlash: 1, tr_flame_mine: 2, tr_rally: 2, tr_decoy_swap: 1,
};

/** Current form + the rest of its family line, with the server's stat formulas (preview copy of DuelBuilder.PetForms). */
export function petForms(cat: Catalog, c: SimCard): PetFormStats[] {
  const f = cat.forms[c.formId];
  const fam = cat.families[f.family];
  return fam.forms.map((id) => ({ id, form: cat.forms[id] })).filter((x) => x.form.stage >= f.stage).sort((a, b) => a.form.stage - b.form.stage).map(({ id, form }) => {
    const s = stats(cat, { ...c, formId: id });
    return { formId: id, stage: fam.forms.length === 1 ? 1 : form.stage, divine: id.startsWith('pet_dv_'), element: form.element, role: form.role,
      hp: s.hp, atk: s.atk, def: s.def, spd: s.spd, critBp: Math.round(s.crit * 10000), skill: form.skill ?? null, ultimate: form.ultimate ?? null, passive: form.passive ?? null };
  });
}

export function playerDeck(cat: Catalog, rules: DuelRules, s: SimState): Omit<DuelCard, 'id'>[] {
  const owned = s.cards.filter((c) => !c.listed).map((c) => ({ c, p: stats(cat, c).power })).sort((a, b) => b.p - a.p);
  const pets: SimCard[] = [];
  for (const { c } of owned) {
    if (pets.length >= 15) break;
    const max = c.divineSet ? rules.config.divineMaxCopies : rules.config.maxCopies;
    if (pets.filter((x) => x.formId === c.formId).length >= max) continue;
    pets.push(c);
  }
  const deck: Omit<DuelCard, 'id'>[] = pets.map((c) => ({ kind: 'pet', cardInstanceId: c.id, forms: petForms(cat, c) }));
  fillTactics(deck, rules.config.deckSize);
  return deck;
}

export function aiDeck(cat: Catalog, rules: DuelRules, level: number, seed: number): Omit<DuelCard, 'id'>[] {
  const first = Object.keys(cat.forms).filter((id) => !id.startsWith('pet_dv_') && cat.forms[id].stage === 1 && cat.families[cat.forms[id].family].forms.length > 1).sort();
  const deck: Omit<DuelCard, 'id'>[] = [];
  for (let i = 0; i < 15; i++) {
    const formId = first[(seed * 7 + Math.floor(i / 2) * 11) % first.length];
    const c: SimCard = { id: -1 - i, serial: '', formId, rarity: 'SR', quality: 'B', finish: 'Normal', level, stars: 3, enhance: 4, breakthrough: 0, awakened: false, skillLevel: 2, divineSet: null, origin: 'ai' };
    deck.push({ kind: 'pet', cardInstanceId: 0, forms: petForms(cat, c) });
  }
  fillTactics(deck, rules.config.deckSize);
  return deck;
}

function fillTactics(deck: Omit<DuelCard, 'id'>[], size: number) {
  const ids = Object.keys(STARTER).sort();
  for (let round = 0; round < 3 && deck.length < size; round++)
    for (const id of ids) { if (deck.length >= size) break; if (STARTER[id] > round) deck.push({ kind: 'tactic', tacticId: id, cardInstanceId: 0, forms: [] }); }
}
