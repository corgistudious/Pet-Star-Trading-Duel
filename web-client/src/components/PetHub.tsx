import { useState } from 'react';
import { api, newIdempotencyKey } from '../api/client';
import type { CardDto, CardUpgradesDto, ResetPreviewDto, UpgradeOptionDto } from '../api/types';
import { useT } from '../i18n';
import { Loading, Modal, useAction, useLoad } from './ui';

type Done = (card: CardDto, currencies?: Record<string, number>) => void;
const TABS = ['star', 'enhance', 'breakthrough', 'awaken', 'skill', 'engrave', 'reset'] as const;

/** Pet Hub: stars, enhancement, breakthrough, awakening, skill, engravings and reset. Costs, gates and the "after"
 *  stats come from the server preview; each confirm sends the value the player saw + an idempotency key. */
export function PetHubModal({ cardId, onClose, onDone }: { cardId: number; onClose: () => void; onDone: Done }) {
  const t = useT();
  const { data, error, reload } = useLoad(() => api.get<CardUpgradesDto>(`/api/cards/${cardId}/upgrades`), [cardId]);
  const { busy, run } = useAction();
  const [tab, setTab] = useState<(typeof TABS)[number]>('star');
  const [reset, setReset] = useState<ResetPreviewDto | null>(null);

  const post = async (url: string, body: unknown) => {
    const r = await run(() => api.post<{ card?: CardDto; currencies?: Record<string, number> } & Partial<CardDto>>(url, body, { idem: newIdempotencyKey() }), t('upgrade.done'));
    await reload();
    if (r) onDone((r.card ?? (r as CardDto)), r.currencies);
  };
  const upgrade = (o: UpgradeOptionDto) => o.kind === 'skill' ? post(`/api/cards/${cardId}/train`, {}) : post(`/api/cards/${cardId}/${o.kind}`, { from: o.current });

  return (
    <Modal title={t('upgrade.title')} onClose={onClose} wide>
      {!data ? <Loading error={error} onRetry={reload} /> : (
        <div className="pet-hub" data-testid="pet-hub">
          <div className="row gap wrap">
            <strong className="stars">{'★'.repeat(data.card.stars ?? 1)}<span className="muted">{'★'.repeat(10 - (data.card.stars ?? 1))}</span></strong>
            {(data.card.enhanceLevel ?? 0) > 0 && <strong>+{data.card.enhanceLevel}</strong>}
            <span className="muted">Lv {data.card.level}/{data.card.levelCap} · {t('card.power')} {data.card.power}</span>
            {data.card.awakened && <span className="tag">{t('upgrade.awakened')}</span>}
            {data.card.divineSet && <span className="tag gold">{t('divine.' + data.card.divineSet)}</span>}
          </div>
          <div className="tabs">{TABS.map((k) => <button key={k} className={'tab' + (tab === k ? ' active' : '')} onClick={() => setTab(k)}>{t('upgrade.' + k)}</button>)}</div>
          {tab === 'engrave' ? (
            <div>
              {[1, 2, 3].map((s) => {
                const e = data.card.engravings?.find((x) => x.slot === s);
                const open = data.unlockedSlots.includes(s);
                return (
                  <div key={s} className="row gap">
                    <span>{t('upgrade.slot', s)}: {!open ? t('upgrade.slot_locked') : e ? `${t('engraving.' + e.type)} Lv${e.level}` : '—'}</span>
                    {e && <button className="btn small" disabled={busy} onClick={() => post(`/api/engravings/${e.id}/upgrade`, { from: e.level })}>Lv+</button>}
                    {e && <button className="btn ghost small" disabled={busy} onClick={() => post(`/api/cards/${cardId}/engravings/unequip`, { slot: s })}>{t('upgrade.unequip')}</button>}
                  </div>
                );
              })}
              {data.freeEngravings.map((e) => {
                const slot = [1, 2, 3].find((s) => data.unlockedSlots.includes(s) && !data.card.engravings?.some((x) => x.slot === s));
                return (
                  <div key={e.id} className="row gap">
                    <span>{t('engraving.' + e.type)} Lv{e.level}</span>
                    <button className="btn small" disabled={busy || !slot} onClick={() => post(`/api/cards/${cardId}/engravings/equip`, { engravingId: e.id, slot })}>{t('upgrade.equip')}</button>
                  </div>
                );
              })}
              <p className="muted small">{t('upgrade.craft')}: 10 × {t('item.engraving_dust')} + 500 ({data.materials.engraving_dust ?? 0})</p>
              <div className="row gap wrap">{['atk', 'def', 'hp', 'spd', 'crit'].map((ty) => <button key={ty} className="btn small" disabled={busy} onClick={() => post('/api/engravings/craft', { type: ty })}>{ty.toUpperCase()}</button>)}</div>
            </div>
          ) : tab === 'reset' ? (
            <div>
              <p className="muted small">{t('upgrade.reset_note')}</p>
              {(['enhance', 'skill'] as const).map((k) => (
                <button key={k} className="btn" onClick={async () => setReset(await api.get<ResetPreviewDto>(`/api/cards/${cardId}/reset-preview?kind=${k}`))}>{t('upgrade.reset_' + k)}</button>
              ))}
              {reset && (
                <div className="panel">
                  <p>{reset.from} → {reset.to} · {t('upgrade.reset_refund')}: {reset.refundBeans} {t('currency.mini_green_bean')}{Object.entries(reset.refundItems).map(([k, v]) => ` · ${v} × ${t('item.' + k)}`).join('')}</p>
                  {reset.blocked ? <p className="error-text">{t('error.' + reset.blocked)}</p> :
                    <button className="btn primary" disabled={busy} onClick={() => { void post(`/api/cards/${cardId}/reset`, { kind: reset.kind, from: reset.from }); setReset(null); }}>{t('upgrade.confirm')}</button>}
                </div>
              )}
            </div>
          ) : (() => {
            const o = data.options.find((x) => x.kind === tab)!;
            return (
              <div>
                <h3>{t('upgrade.' + o.kind)}: {o.current} / {o.max}</h3>
                {o.kind === 'enhance' && <p className="muted small">{t('upgrade.always_succeeds')}</p>}
                {!o.cost ? <p>{t('upgrade.max')}</p> : (
                  <>
                    <p>{t('upgrade.cost')}: {o.cost.beans} {t('currency.mini_green_bean')} ({data.beans})
                      {Object.entries(o.cost.items).map(([k, v]) => <span key={k}> · {v} × {t('item.' + k)} ({data.materials[k] ?? 0})</span>)}</p>
                    {o.preview && <p>{t('upgrade.preview')}: {t('card.power')} {data.card.power} → <strong>{o.preview.power}</strong> · ATK {data.card.atk} → {o.preview.atk} · HP {data.card.hp} → {o.preview.hp}</p>}
                    {o.blocked && <p className="error-text">{t('error.' + (o.blocked.startsWith('not_enough_') && o.blocked !== 'not_enough_beans' ? 'insufficient_items' : o.blocked))}</p>}
                    <button className="btn primary big" disabled={busy || !!o.blocked} onClick={() => upgrade(o)} data-testid={`upgrade-${o.kind}`}>{t('upgrade.confirm')}</button>
                  </>
                )}
              </div>
            );
          })()}
        </div>
      )}
    </Modal>
  );
}
