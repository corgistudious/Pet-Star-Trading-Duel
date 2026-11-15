// Dependency-free SVG line chart for valuation history (estimated value per day) with trade dots.
import { useT } from '../i18n';
import { fmt } from './ui';

export interface ChartPoint { t: number; v: number }

export function buildPath(points: ChartPoint[], w: number, h: number, pad = 8): { d: string; min: number; max: number; xy: [number, number][] } {
  if (points.length === 0) return { d: '', min: 0, max: 0, xy: [] };
  const ts = points.map((p) => p.t), vs = points.map((p) => p.v);
  const t0 = Math.min(...ts), t1 = Math.max(...ts), min = Math.min(...vs), max = Math.max(...vs);
  const sx = (x: number) => pad + (t1 === t0 ? (w - 2 * pad) / 2 : ((x - t0) / (t1 - t0)) * (w - 2 * pad));
  const sy = (y: number) => h - pad - (max === min ? (h - 2 * pad) / 2 : ((y - min) / (max - min)) * (h - 2 * pad));
  const xy = points.map((p) => [sx(p.t), sy(p.v)] as [number, number]);
  return { d: xy.map(([x, y], i) => `${i ? 'L' : 'M'}${x.toFixed(1)},${y.toFixed(1)}`).join(' '), min, max, xy };
}

export function PriceChart({ series, trades, height = 160 }: { series: ChartPoint[]; trades?: ChartPoint[]; height?: number }) {
  const t = useT();
  const w = 320;
  const all = [...series, ...(trades ?? [])];
  if (all.length === 0) return <div className="chart-empty muted">{t('price.no_data')}</div>;
  const { d, min, max } = buildPath(series.length ? series : all, w, height);
  const dots = buildPath(all, w, height).xy.slice(series.length);
  return (
    <figure className="chart">
      <svg viewBox={`0 0 ${w} ${height}`} role="img" aria-label={t('price.history')} preserveAspectRatio="none">
        <path d={d} fill="none" stroke="var(--accent)" strokeWidth="2.5" strokeLinejoin="round" strokeLinecap="round" />
        {dots.map(([x, y], i) => <circle key={i} cx={x} cy={y} r="3" fill="var(--gold)" />)}
      </svg>
      <figcaption className="muted small">{fmt(min)} – {fmt(max)}</figcaption>
    </figure>
  );
}
