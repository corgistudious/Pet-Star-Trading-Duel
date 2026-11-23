// Leaderboards (Preview): podium Top 1 centre/gold, Top 2 left/silver, Top 3 right/bronze; 17 boards; periods; my rank.
import { useState } from 'react';
import { Avatar } from './Cosmetics';
import { GuildBadge } from './Guild';
import type { Hub } from './hub';
import { BOARDS, GUILD_BOARDS, guildOf, leaderboard, type Board, type Row } from './world';

export const boardName = (b: Board, P: Hub['P']) => ({
  pvp_ranked: P('PvP Xếp hạng', 'PvP Ranked'), pvp_wins: P('PvP Thắng', 'PvP Wins'), pvp_winrate: P('Tỷ lệ thắng', 'Win Rate'), collection: P('Sưu tập Pet', 'Pet Collection'),
  rare_collector: P('Sưu tập hiếm', 'Rare Collector'), evolution: P('Bậc thầy Tiến hóa', 'Evolution Master'), pve_progress: P('Tiến độ PvE', 'PvE Progress'), boss_damage: P('Sát thương Boss', 'Boss Damage'),
  trainer_level: P('Cấp Trainer', 'Trainer Level'), achievements: P('Điểm thành tích', 'Achievement Points'), minigames: P('Cao thủ Mini Game', 'Mini Game Masters'),
  guild_ranking: P('Xếp hạng Guild', 'Guild Ranking'), guild_contribution: P('Đóng góp Guild', 'Guild Contribution'), guild_boss: 'Guild Boss', guild_war: 'Guild War',
  showcase_liked: P('Showcase được yêu thích', 'Most-loved Showcase'), creators: P('Nhà sáng tạo nổi bật', 'Top Pet Creators'),
} as Record<Board, string>)[b];

export function Leaderboards({ h }: { h: Hub }) {
  const { P, w, me } = h;
  const [board, setBoard] = useState<Board>('pvp_ranked');
  const [period, setPeriod] = useState<'week' | 'month' | 'season' | 'all'>('all');
  const rows = leaderboard(w, board, period);
  const isGuild = GUILD_BOARDS.includes(board);
  const myKey = isGuild ? guildOf(w, me)?.id : me;
  const myIdx = rows.findIndex((r) => r.id === myKey);
  const fmt = (r: Row) => (board === 'pvp_winrate' ? `${(r.score / 100).toFixed(1)}%` : r.score.toLocaleString());
  const podium = [rows[1], rows[0], rows[2]];
  return (
    <div className="ld">
      <div className="ld-boards">{BOARDS.map((b) => <button key={b} className={board === b ? 'on' : ''} onClick={() => setBoard(b)} data-testid={`lb-${b}`}>{boardName(b, P)}</button>)}</div>
      <div className="sh-chips">{(['week', 'month', 'season', 'all'] as const).map((p) => <button key={p} className={period === p ? 'on' : ''} onClick={() => setPeriod(p)}>{({ week: P('Tuần', 'Week'), month: P('Tháng', 'Month'), season: P('Mùa', 'Season'), all: P('Toàn thời gian', 'All time') })[p]}</button>)}</div>
      <div className="ld-podium" data-testid="podium">
        {podium.map((r, i) => { const place = [2, 1, 3][i];
          return <div key={i} className={'ld-step p' + place}>
            {r ? <>{isGuild ? <GuildBadge g={r.guild!} size={place === 1 ? 64 : 52} /> : <Avatar a={r.acc} d={h.d} size={place === 1 ? 70 : 56} onClick={() => h.openProfile(r.id)} />}
              <b className="ellipsis" title={r.name}>{r.name}</b>{!isGuild && guildOf(w, r.id)?.tag && <small>[{guildOf(w, r.id)!.tag}]</small>}<small className="ld-score">{fmt(r)}</small></>
              : <small className="muted">—</small>}
            <span className="ld-block">{place}</span>
          </div>; })}
      </div>
      <div className="ld-list">{rows.slice(3, 50).map((r, i) => <div key={r.id} className={'lp-rank' + (r.id === myKey ? ' me' : '')}><b>#{i + 4}</b><span className="ellipsis">{r.name}</span><small>{fmt(r)}{r.extra ? ` · ${r.extra}` : ''}</small></div>)}</div>
      <div className="lp-rank me sticky">{myIdx >= 0 ? <><b>#{myIdx + 1}</b><span>{P('Hạng của tôi', 'My rank')}</span><small>{fmt(rows[myIdx])}</small></> : <span>{P('Bạn chưa có điểm ở bảng này', 'You have no score on this board yet')}</span>}</div>
      <small className="muted">{P('Chỉ dùng dữ liệu thật của các tài khoản Preview (không có đối thủ giả). Hòa điểm: ai đạt trước (id nhỏ hơn) xếp trên. Bảng xã hội chỉ tính lượt thích từ người khác, mỗi người một lần. Bản thật tính trên máy chủ, có lịch sử mùa và phần thưởng.', 'Only real Preview-account data (no fake rivals). Ties: the earlier id ranks higher. Social boards count likes from other players once each. The real game computes boards on the server, with season history and rewards.')}</small>
    </div>
  );
}
