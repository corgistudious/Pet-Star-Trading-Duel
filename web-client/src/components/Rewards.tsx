import type { RewardBundle } from '../api/types';
import { useT } from '../i18n';
import { useCatalog } from '../state/catalog';
import { CurrencyIcon, fmt } from './ui';

/** Human-readable reward bundle (currencies, items, packs, cards). */
export function RewardList({ r }: { r: RewardBundle | null | undefined }) {
  const t = useT();
  const cat = useCatalog();
  if (!r) return null;
  return (
    <span className="reward-list">
      {Object.entries(r.currencies ?? {}).map(([c, v]) => <span key={c} className="rw"><CurrencyIcon currency={c} size={16} />{fmt(v)}</span>)}
      {Object.entries(r.items ?? {}).map(([c, v]) => <span key={c} className="rw">{t('item.' + c)} ×{v}</span>)}
      {Object.entries(r.packs ?? {}).map(([c, v]) => <span key={c} className="rw">🎴 {t(packKey(c))} ×{v}</span>)}
      {(r.cards ?? []).map((c, i) => <span key={i} className="rw">🃏 {cat.forms[c.formId]?.name ?? c.formId} {c.rarity}</span>)}
    </span>
  );
}
export const packKey = (id: string) => `pack.${id.replace(/_pack$/, '')}.name`;
