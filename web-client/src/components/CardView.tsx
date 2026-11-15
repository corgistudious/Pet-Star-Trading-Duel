// 9:16 pet card following assets-source/card-template/card-layout.json (1080x1920 reference canvas).
// Positions are percentages of that canvas, text scales with container-query units, so one component serves
// the 110px grid tile and the full-screen reveal. Only artwork, texts, stats, rarity color and finish change.
import type { CSSProperties } from 'react';
import type { CardDto } from '../api/types';
import { useI18n } from '../i18n';
import { ELEMENT_COLORS, RARITY_COLORS, petArt, useCatalog } from '../state/catalog';

const R = (x: number, y: number, w: number, h: number): CSSProperties => ({
  left: `${(x / 1080) * 100}%`, top: `${(y / 1920) * 100}%`, width: `${(w / 1080) * 100}%`, height: `${(h / 1920) * 100}%`,
});

export const ELEMENT_GLYPH: Record<string, string> = { fire: '🔥', water: '💧', nature: '🍃', earth: '⛰️', wind: '🌀', lightning: '⚡', light: '✨', dark: '🌙' };

export function CardView({ card, compact, onClick, selected, badge }: { card: CardDto; compact?: boolean; onClick?: () => void; selected?: boolean; badge?: string }) {
  const { t, lang } = useI18n();
  const cat = useCatalog();
  const color = RARITY_COLORS[card.rarity] ?? '#9CA3AF';
  const skill = card.skillId ? cat.skills[card.skillId] : undefined;
  const ult = card.ultimateId ? cat.skills[card.ultimateId] : undefined;
  const style = { '--rarity': color, '--element': ELEMENT_COLORS[card.element] ?? '#888' } as CSSProperties;
  const Tag = onClick ? 'button' : 'div';
  return (
    <Tag className={`pcard finish-${card.finish.toLowerCase()} rar-${card.rarity} ${compact ? 'compact' : ''} ${selected ? 'selected' : ''}`}
      style={style} onClick={onClick} aria-label={`${card.name} ${card.rarity} Lv.${card.level}`} type={onClick ? 'button' : undefined}>
      <div className="pcard-inner">
        <div className="pc-namebar" style={R(70, 56, 940, 124)}>
          <span className="pc-name">{card.name}</span>
        </div>
        <span className="pc-element" style={R(890, 70, 96, 96)} title={t('element.' + card.element)}>{ELEMENT_GLYPH[card.element] ?? '•'}</span>
        <span className="pc-chip pc-rarity" style={R(70, 200, 210, 72)}>{card.rarity}</span>
        <span className="pc-chip pc-quality" style={R(296, 200, 150, 72)}>{card.quality}</span>
        <span className="pc-chip pc-level" style={R(790, 200, 220, 72)}>Lv.{card.level}</span>
        <div className="pc-art" style={R(70, 292, 940, 860)}>
          <img src={petArt(card.formId, compact)} alt="" loading="lazy" decoding="async" draggable={false} />
          {card.finish !== 'Normal' && <div className="pc-finish" />}
        </div>
        <div className="pc-stat" style={R(70, 1178, 300, 150)}><small>HP</small><b>{card.hp}</b></div>
        <div className="pc-stat" style={R(390, 1178, 300, 150)}><small>ATK</small><b>{card.atk}</b></div>
        <div className="pc-stat" style={R(710, 1178, 300, 150)}><small>DEF</small><b>{card.def}</b></div>
        {!compact && (
          <div className="pc-skills" style={R(70, 1352, 940, 420)}>
            {skill && <div className="pc-skill"><b>{skill.name[lang] ?? skill.name.en}</b><span className="pc-cost">⚡{skill.cost}</span></div>}
            {skill && <p className="pc-skilltext">{t('target.' + skill.target)}{card.skillLevel > 1 ? ` · Lv.${card.skillLevel}` : ''}</p>}
            {ult && <div className="pc-skill ult"><b>★ {ult.name[lang] ?? ult.name.en}</b><span className="pc-cost">⚡{ult.cost}</span></div>}
          </div>
        )}
        <div className="pc-footer" style={R(70, 1796, 940, 70)}>
          <span>{card.serial || ' '}</span><span>{t('role.' + card.role)}</span>
        </div>
      </div>
      {(card.isLocked || card.isFavorite || card.isBusy || badge) && (
        <div className="pc-flags">{card.isFavorite && '❤'}{card.isLocked && '🔒'}{card.isBusy && '⏳'}{badge && <span>{badge}</span>}</div>
      )}
    </Tag>
  );
}

/** Hidden card back (pack opening). */
export function CardBack() {
  return <div className="pcard back"><div className="pcard-inner"><div className="back-star">★</div></div></div>;
}
