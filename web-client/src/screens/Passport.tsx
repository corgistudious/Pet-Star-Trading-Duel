import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, qs } from '../api/client';
import type { PassportDto } from '../api/types';
import { useI18n } from '../i18n';
import { PageHead } from '../components/Layout';
import { CardView } from '../components/CardView';
import { PriceChart } from '../components/PriceChart';
import { Loading, Tabs, fmt, useLoad } from '../components/ui';
import { useCatalog } from '../state/catalog';

interface History { snapshots: { day: string; currency: string; estimatedValue: number }[]; trades: { at: string; currency: string; price: number }[] }

export default function Passport() {
  const { serial } = useParams();
  const { t, lang } = useI18n();
  const cat = useCatalog();
  const { data, error, reload } = useLoad(() => api.get<PassportDto>(`/api/passport/${serial}`), [serial]);
  const [cur, setCur] = useState<'MINI_GREEN_BEAN' | 'RAINBOW_STAR'>('MINI_GREEN_BEAN');
  const hist = useLoad(() => (data ? api.get<History>('/api/valuation/history' + qs({ formId: data.card.formId, rarity: data.card.rarity, currency: cur, days: 90 })) : Promise.resolve(undefined)), [data?.card.formId, data?.card.rarity, cur]);
  if (!data) return <div className="page"><PageHead title={t('passport.title')} back /><Loading error={error} onRetry={reload} /></div>;
  const date = (s: string) => new Date(s).toLocaleDateString(lang);
  const name = (f?: string) => (f ? cat.forms[f]?.name ?? f : '');
  const section = (title: string, rows: { at: string; text: string }[]) => (
    <section className="pp-section"><h4>{title}</h4>
      {rows.length === 0 ? <p className="muted small">{t('market.no_history')}</p> : <ul>{rows.map((r, i) => <li key={i}><span className="muted">{date(r.at)}</span> {r.text}</li>)}</ul>}
    </section>
  );
  return (
    <div className="page">
      <PageHead title={t('passport.title')} back />
      <div className="passport">
        <div className="pp-head">{t('passport.heading')}</div>
        <div className="pp-top">
          <div className="pp-card"><CardView card={data.card} compact /></div>
          <div>
            <div className="serial big">#{data.serial}</div>
            <p>{t('passport.issued', data.issuedServer, date(data.issuedAt), t('origin.' + data.origin))}</p>
            <p>{t('passport.owner', data.ownerNickname, data.transferCount)}</p>
            <p className="muted small">{t('web.form_at_issue')}: {name(data.formAtIssue)} · {t('web.lock_' + data.lockStatus)}</p>
          </div>
        </div>
        <section className="pp-section"><h4>{t('passport.values')}</h4>
          {data.values.map((v) => <p key={v.currency}>{t('passport.value_line', t('currency.' + v.currency.toLowerCase()), fmt(v.initial), fmt(v.estimated), t('confidence.' + v.confidence))}</p>)}
          <p className="muted small">{t('market.reference_disclaimer')}</p>
        </section>
        <section className="pp-section"><h4>{t('price.history')}</h4>
          <Tabs value={cur} onChange={setCur} items={[['MINI_GREEN_BEAN', t('currency.mini_green_bean')], ['RAINBOW_STAR', t('currency.rainbow_star')]]} />
          <PriceChart series={(hist.data?.snapshots ?? []).map((s) => ({ t: Date.parse(s.day), v: s.estimatedValue }))}
            trades={(hist.data?.trades ?? []).map((x) => ({ t: Date.parse(x.at), v: x.price }))} />
        </section>
        {section(t('passport.evolutions'), data.evolutions.map((e) => ({ at: e.at, text: `${name(e.fromFormId)} → ${name(e.toFormId)}` })))}
        {section(t('passport.upgrades'), data.upgrades.map((e) => ({ at: e.at, text: e.detail ?? e.type })))}
        {section(t('passport.listings'), data.listings.map((e) => ({ at: e.at, text: `${e.status ?? ''} ${e.price != null ? fmt(e.price) : ''} ${e.currency ? t('currency.' + e.currency.toLowerCase()) : ''}` })))}
        {section(t('passport.trades'), data.trades.map((e) => ({ at: e.at, text: `${e.type} ${e.price != null ? fmt(e.price) : ''} ${e.currency ? t('currency.' + e.currency.toLowerCase()) : ''}` })))}
      </div>
    </div>
  );
}
