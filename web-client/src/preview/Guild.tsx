// Guild (Preview): browse/create/join, Guild Hall, missions, Guild Boss, Guild War, guild chat and posts, management.
import { useState } from 'react';
import { petArt } from '../state/catalog';
import { Avatar } from './Cosmetics';
import { ChatPanel } from './ChatDock';
import { ago, errText, type Hub } from './hub';
import { PostCard } from './SocialHub';
import {
  acc, announce, BOSSES, bossHit, claimGuildMission, createGuild, currentBoss, dayKey, decideRequest, dissolve, feed, GUILD_LEVELS, guildInvite, guildMissionView, guildOf,
  joinGuild, kick, leaveGuild, maxMembers, online, ROLES, roleRank, setRole, warRegister, weekKey, createPost, type Role, type WGuild,
} from './world';

export const EMBLEMS: Record<string, string> = { emblem_star: '★', emblem_flame: '🔥', emblem_leaf: '🍃', emblem_wave: '🌊', emblem_bolt: '⚡', emblem_moon: '🌙', emblem_crown: '♛', emblem_paw: '🐾' };
export const BANNERS: Record<string, string> = { banner_sky: 'linear-gradient(135deg,#7cc4ff,#4a6cf7)', banner_sunset: 'linear-gradient(135deg,#ffb36b,#e8467c)', banner_forest: 'linear-gradient(135deg,#7ed98a,#2e8b57)',
  banner_ocean: 'linear-gradient(135deg,#5ee0e6,#1d6fa5)', banner_aurora: 'linear-gradient(135deg,#7cf5d8,#7c8cff,#ff7ce0)', banner_night: 'linear-gradient(135deg,#3b3f8f,#151a3d)' };
export const roleName = (r: Role, P: Hub['P']) => ({ leader: 'Guild Master', vice: 'Vice Master', officer: 'Officer', elite: 'Elite', member: P('Thành viên', 'Member') })[r];
export function GuildBadge({ g, size = 54 }: { g: Pick<WGuild, 'emblem' | 'banner'>; size?: number }) {
  return <span className="gd-emblem" style={{ width: size, height: size, background: BANNERS[g.banner], fontSize: size * 0.5 }} aria-hidden="true">{EMBLEMS[g.emblem] ?? '★'}</span>;
}

type GTab = 'hall' | 'missions' | 'boss' | 'war' | 'chat' | 'posts' | 'manage';

export function GuildPanel({ h }: { h: Hub }) {
  const { P, w, me } = h;
  const g = guildOf(w, me);
  const [tab, setTab] = useState<GTab>('hall');
  if (!g) return <NoGuild h={h} />;
  const role = g.members.find((m) => m.uid === me)!.role;
  const tabs: [GTab, string][] = [['hall', P('Sảnh Guild', 'Guild Hall')], ['missions', P('Nhiệm vụ', 'Missions')], ['boss', 'Boss'], ['war', 'Guild War'], ['chat', 'Chat'], ['posts', P('Bài Guild', 'Posts')], ['manage', P('Quản lý', 'Manage')]];
  return (
    <div className="gd">
      <div className="gd-banner" style={{ background: BANNERS[g.banner] }}>
        <GuildBadge g={g} size={64} />
        <div className="grow"><b className="ellipsis">{g.tag && `[${g.tag}] `}{g.name}</b><small>Lv {g.level} · {g.members.length}/{maxMembers(g.level)} · 🟢 {g.members.filter((m) => online(acc(w, m.uid))).length} online · {roleName(role, P)}</small>
          <span className="gd-exp"><i style={{ width: `${Math.min(100, 100 * (g.exp - (GUILD_LEVELS[g.level - 1] ?? 0)) / Math.max(1, (GUILD_LEVELS[g.level] ?? g.exp + 1) - (GUILD_LEVELS[g.level - 1] ?? 0)))}%` }} /></span></div>
      </div>
      <nav className="sh-chips">{tabs.map(([k, l]) => <button key={k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)} data-testid={`gd-${k}`}>{l}</button>)}</nav>
      {tab === 'hall' && <Hall h={h} g={g} />}
      {tab === 'missions' && <Missions h={h} g={g} />}
      {tab === 'boss' && <Boss h={h} g={g} />}
      {tab === 'war' && <War h={h} g={g} role={role} />}
      {tab === 'chat' && <ChatPanel h={h} channel={{ kind: 'guild' }} />}
      {tab === 'posts' && <GuildPosts h={h} />}
      {tab === 'manage' && <Manage h={h} g={g} role={role} />}
    </div>
  );
}

function NoGuild({ h }: { h: Hub }) {
  const { P, w, me, say } = h;
  const [q, setQ] = useState('');
  const [f, setF] = useState({ name: '', tag: '', emblem: 'emblem_star', banner: 'banner_sky', visibility: 'public' as 'public' | 'private', policy: 'open' as 'open' | 'approval', minLevel: 1, desc: '' });
  const act = (fn: () => unknown, ok: string) => { try { const r = fn(); say(r === 'requested' ? P('Đã gửi yêu cầu, chờ duyệt', 'Request sent, awaiting approval') : ok); } catch (e) { say(errText(e, P)); } };
  const list = w.guilds.filter((g) => g.visibility === 'public' && (!q.trim() || g.name.toLowerCase().includes(q.toLowerCase()) || g.tag.includes(q.toUpperCase())));
  const invites = w.guilds.filter((g) => g.invites.includes(me));
  return (
    <div className="lp-col">
      {invites.length > 0 && <><b>✉️ {P('Lời mời vào Guild', 'Guild invites')}</b>{invites.map((g) => <div key={g.id} className="lp-friend"><GuildBadge g={g} size={40} /><span className="grow"><b>{g.name}</b></span><button className="btn small primary" onClick={() => act(() => joinGuild(me, g.id), P('Đã vào Guild', 'Joined'))} data-testid={`gd-accept-${g.id}`}>{P('Tham gia', 'Join')}</button></div>)}</>}
      <b>🔎 {P('Tìm Guild', 'Find a guild')}</b>
      <input className="lb-input" value={q} onChange={(e) => setQ(e.target.value)} placeholder={P('Tên hoặc tag', 'Name or tag')} data-testid="gd-search" />
      {list.length === 0 && <small className="muted">{P('Chưa có Guild công khai nào. Hãy lập Guild đầu tiên!', 'No public guilds yet. Found the first one!')}</small>}
      {list.map((g) => <div key={g.id} className="lp-friend"><GuildBadge g={g} size={40} /><span className="grow"><b className="ellipsis">{g.tag && `[${g.tag}] `}{g.name}</b><small>Lv {g.level} · {g.members.length}/{maxMembers(g.level)} · {g.joinPolicy === 'open' ? P('Tự do', 'Open') : P('Cần duyệt', 'Approval')} · Lv≥{g.minLevel}</small></span>
        <button className="btn small primary" onClick={() => act(() => joinGuild(me, g.id), P('Đã vào Guild', 'Joined'))} data-testid={`gd-join-${g.id}`}>{g.joinPolicy === 'open' ? P('Tham gia', 'Join') : P('Xin vào', 'Request')}</button></div>)}
      <b>🏰 {P('Lập Guild', 'Found a guild')}</b>
      <div className="gd-form">
        <input className="lb-input" maxLength={24} value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder={P('Tên Guild (3–24)', 'Guild name (3–24)')} data-testid="gd-name" />
        <input className="lb-input" maxLength={5} value={f.tag} onChange={(e) => setF({ ...f, tag: e.target.value.toUpperCase() })} placeholder="TAG" data-testid="gd-tag" />
        <div className="gd-picks">{Object.keys(EMBLEMS).map((k) => <button key={k} className={f.emblem === k ? 'on' : ''} onClick={() => setF({ ...f, emblem: k })} aria-label={k}><GuildBadge g={{ emblem: k, banner: f.banner }} size={34} /></button>)}</div>
        <div className="gd-picks">{Object.keys(BANNERS).map((k) => <button key={k} className={f.banner === k ? 'on' : ''} onClick={() => setF({ ...f, banner: k })} style={{ background: BANNERS[k] }} aria-label={k} />)}</div>
        <select className="lb-input small" value={f.visibility} onChange={(e) => setF({ ...f, visibility: e.target.value as 'public' | 'private' })}><option value="public">{P('Công khai', 'Public')}</option><option value="private">{P('Riêng tư (chỉ mời)', 'Private (invite only)')}</option></select>
        <select className="lb-input small" value={f.policy} onChange={(e) => setF({ ...f, policy: e.target.value as 'open' | 'approval' })}><option value="open">{P('Vào tự do', 'Open join')}</option><option value="approval">{P('Cần duyệt', 'Needs approval')}</option></select>
        <label className="small">{P('Cấp tối thiểu', 'Min level')} <input className="lb-input small" type="number" min={1} max={100} value={f.minLevel} onChange={(e) => setF({ ...f, minLevel: Number(e.target.value) || 1 })} /></label>
        <textarea className="lb-input" rows={2} maxLength={300} value={f.desc} onChange={(e) => setF({ ...f, desc: e.target.value })} placeholder={P('Mô tả', 'Description')} />
        <button className="btn primary" onClick={() => act(() => createGuild(me, f.name, f.tag, f.emblem, f.banner, f.visibility, f.policy, f.minLevel, f.desc), P('Đã lập Guild!', 'Guild founded!'))} data-testid="gd-create">{P('Lập Guild', 'Create guild')}</button>
      </div>
    </div>
  );
}

function Hall({ h, g }: { h: Hub; g: WGuild }) {
  const { P, w } = h;
  const members = [...g.members].sort((a, b) => roleRank(b.role) - roleRank(a.role) || (g.contrib[b.uid] ?? 0) - (g.contrib[a.uid] ?? 0));
  return (
    <div className="gd-hall">
      <div className="lp-col">
        {g.desc && <p className="sh-text">{g.desc}</p>}
        <b>📢 {P('Thông báo', 'Announcements')}</b>
        {g.announcements.length === 0 ? <small className="muted">—</small> : g.announcements.map((a, i) => <div key={i} className="lp-card"><small>{acc(w, a.by)?.name} · {ago(a.at, P)}</small><span>{a.text}</span></div>)}
        <b>📜 {P('Hoạt động gần đây', 'Recent activity')}</b>
        {g.logs.slice(0, 12).map((l, i) => <small key={i}>{ago(l.at, P)} · {l.text}</small>)}
      </div>
      <div className="lp-col">
        <b>👥 {P('Thành viên & đóng góp', 'Members & contribution')}</b>
        {members.map((m, i) => { const a = acc(w, m.uid);
          return <div key={m.uid} className="lp-friend"><em className="gd-rank">#{i + 1}</em><Avatar a={a} d={h.d} size={36} onClick={() => h.openProfile(m.uid)} /><span className="grow"><b className="ellipsis">{a?.name}</b><small>{roleName(m.role, P)} · {online(a) ? '🟢' : '⚪'}</small></span><b>{g.contrib[m.uid] ?? 0}</b></div>; })}
      </div>
    </div>
  );
}

function Missions({ h, g }: { h: Hub; g: WGuild }) {
  const { P, me, say, setS } = h;
  const list = guildMissionView(g, me);
  const cat: Record<string, string> = { pve: 'PvE', pvp: 'PvP', collection: P('Sưu tập', 'Collection'), community: P('Cộng đồng', 'Community') };
  const name: Record<string, [string, string]> = { pve_win: ['Thắng trận PvE', 'Win PvE battles'], pvp_battle: ['Đấu PvP', 'Play PvP'], pvp_win: ['Thắng PvP', 'Win PvP'], pack_open: ['Mở gói thẻ', 'Open packs'],
    post: ['Đăng bài', 'Post on the feed'], evolve: ['Tiến hóa Pet', 'Evolve Pets'], boss_attack: ['Đánh Boss Guild', 'Attack the Guild Boss'], showcase: ['Khoe Pet', 'Post showcases'] };
  return (
    <div className="lp-col">
      <small className="muted">{P('Tiến độ cộng dồn từ hành động thật của mọi thành viên. Mỗi người nhận thưởng 1 lần/kỳ; người nhận đầu tiên cộng EXP Guild.', 'Progress adds up from every member\'s real actions. Each member claims once per period; the first claim adds guild EXP.')}</small>
      {(['daily', 'weekly'] as const).map((per) => <div key={per} className="lp-col"><b>{per === 'daily' ? P('Hằng ngày', 'Daily') : P('Hằng tuần', 'Weekly')}</b>
        {list.filter((m) => m.period === per).map((m) => <div key={m.id} className="lp-card row"><span className="grow"><b>{P(...(name[m.kind] ?? [m.kind, m.kind]))}</b><small>{cat[m.category]} · {m.progress}/{m.target} · +{m.reward.MINI_GREEN_BEAN ?? 0} {P('Đậu Xanh Mini', 'Beans')} · +{m.guildExp} EXP</small>
          <span className="gd-exp"><i style={{ width: `${100 * m.progress / m.target}%` }} /></span></span>
          <button className="btn small primary" disabled={!m.completed || m.claimed} onClick={() => { try { const b = claimGuildMission(me, m.id); setS((x) => ({ ...x, beans: x.beans + b })); say(`+${b} ${P('Đậu Xanh Mini', 'Beans')}`); } catch (e) { say(errText(e, P)); } }} data-testid={`gm-${m.id}`}>{m.claimed ? '✅' : P('Nhận', 'Claim')}</button></div>)}</div>)}
    </div>
  );
}

function Boss({ h, g }: { h: Hub; g: WGuild }) {
  const { P, me, say, w } = h;
  const b = currentBoss(g);
  const def = BOSSES.find((x) => x.id === b.bossId)!;
  const mine = b.hits.filter((x) => x.uid === me && x.day === dayKey()).length;
  const board = Object.entries(b.hits.reduce<Record<number, number>>((m, x) => { m[x.uid] = (m[x.uid] ?? 0) + x.dmg; return m; }, {})).sort((x, y) => y[1] - x[1]);
  const attack = () => {
    const dmg = h.bossFight(def.formId, g.level);
    if (dmg == null) return say(P('Cần đội hình 3 Pet và dữ liệu trận đấu', 'Needs a 3-Pet team and battle data'));
    try { const r = bossHit(me, dmg); say(`${P('Gây', 'Dealt')} ${dmg.toLocaleString()} ${P('sát thương', 'damage')}${r.defeated ? ' · 🏆 ' + P('Boss đã bị hạ!', 'Boss defeated!') : ''}`); } catch (e) { say(errText(e, P)); }
  };
  return (
    <div className="gd-boss">
      <div className="gd-boss-art"><img src={petArt(def.formId, true)} alt="" /><b>{b.bossId === 'gboss_ember_titan' ? P('Titan Than Hồng', 'Ember Titan') : P('Khổng Lồ Thủy Triều', 'Tide Colossus')}</b>
        <span className="gd-hp"><i style={{ width: `${100 * b.hp / b.maxHp}%` }} /><em>{b.hp.toLocaleString()} / {b.maxHp.toLocaleString()}</em></span>
        <small>{P('Tuần', 'Week')} {weekKey()} · {P('HP chung của cả Guild', 'Shared guild HP')}</small>
        <button className="btn primary" disabled={b.hp <= 0 || mine >= 3} onClick={attack} data-testid="boss-attack">⚔ {P('Tấn công', 'Attack')} ({3 - mine}/3)</button>
        <small className="muted">{P('Sát thương = tổng sát thương đội 3 Pet của bạn gây ra trong 8 hiệp, tính bằng engine 3v3 (AI hai phía). Bản thật tính trên máy chủ.', 'Damage = total dealt by your 3-Pet team in 8 rounds, computed with the 3v3 engine (AI on both sides). The real game computes it on the server.')}</small></div>
      <div className="lp-col"><b>🏅 {P('Bảng đóng góp sát thương', 'Damage board')}</b>
        {board.length === 0 && <small className="muted">—</small>}
        {board.map(([uid, dmg], i) => <div key={uid} className="lp-rank"><b>#{i + 1}</b><span className="ellipsis">{acc(w, Number(uid))?.name}</span><small>{dmg.toLocaleString()}</small></div>)}
        <small className="muted">{P('Hạ Boss: +1.500 EXP Guild. Lịch sử theo tuần lưu cùng Guild.', 'Defeat: +1,500 guild EXP. Weekly history is kept with the guild.')}</small></div>
    </div>
  );
}

function War({ h, g, role }: { h: Hub; g: WGuild; role: Role }) {
  const { P, w, me, say } = h;
  const war = g.war?.key === weekKey() ? g.war : null;
  const opp = war?.opponent != null ? w.guilds.find((x) => x.id === war.opponent) : null;
  const defenders = opp ? opp.members.map((m) => acc(w, m.uid)).filter((a) => a && a.team.length === 3) : [];
  const standings = w.guilds.filter((x) => x.war?.key === weekKey()).sort((a, b) => b.war!.points - a.war!.points);
  return (
    <div className="lp-col">
      <small className="muted">{P('Đăng ký theo tuần → ghép Guild cùng tuần có cấp gần nhất → mỗi trận 3v3 thắng +3, hòa +1 điểm cho Guild.', 'Register weekly → paired with the closest-level guild that week → each 3v3 win +3, draw +1 point for your guild.')}</small>
      {!war ? <button className="btn primary" disabled={roleRank(role) < 3} onClick={() => { try { warRegister(me); say(P('Đã đăng ký Guild War', 'Registered for Guild War')); } catch (e) { say(errText(e, P)); } }} data-testid="war-register">{P('Đăng ký Guild War', 'Register for Guild War')} {roleRank(role) < 3 ? `(${P('cần Officer', 'Officer+')})` : ''}</button>
        : <div className="lp-card"><b>⚔ {P('Tuần', 'Week')} {war.key}</b><small>{P('Điểm', 'Points')} {war.points} · {war.wins}/{war.battles} {P('thắng', 'wins')}</small>
          {opp ? <><small>{P('Đối thủ', 'Opponent')}: <b>{opp.tag && `[${opp.tag}] `}{opp.name}</b> · {opp.war?.points ?? 0} {P('điểm', 'pts')}</small>
            {defenders.length === 0 ? <small className="muted">{P('Đối thủ chưa có thành viên nào lưu đội hình.', 'No opponent member has saved a team yet.')}</small>
              : <div className="lp-row wrap">{defenders.map((a) => <button key={a!.uid} className="btn small" onClick={() => h.startWar(a!.team, a!.name)} data-testid={`war-fight-${a!.uid}`}>⚔ {a!.name}</button>)}</div>}</>
            : <small className="muted">{P('Đang chờ Guild khác đăng ký để ghép cặp.', 'Waiting for another guild to register.')}</small>}</div>}
      <b>🏆 {P('Bảng điểm tuần', 'Weekly standings')}</b>
      {standings.map((x, i) => <div key={x.id} className="lp-rank"><b>#{i + 1}</b><span className="ellipsis">{x.tag && `[${x.tag}] `}{x.name}</span><small>{x.war!.points}</small></div>)}
      {g.warHistory.length > 0 && <><b>{P('Lịch sử', 'History')}</b>{g.warHistory.map((x, i) => <small key={i}>{x.key} · {x.points} {P('điểm', 'pts')}</small>)}</>}
    </div>
  );
}

function GuildPosts({ h }: { h: Hub }) {
  const { P, w, me, say } = h;
  const [t, setT] = useState('');
  return (
    <div className="lp-col">
      <div className="sh-row"><input className="lb-input" maxLength={500} value={t} onChange={(e) => setT(e.target.value)} placeholder={P('Bài chỉ thành viên Guild thấy…', 'Members-only post…')} data-testid="gd-post-text" />
        <button className="btn small primary" disabled={!t.trim()} onClick={() => { try { createPost(me, { type: 'text', caption: t, visibility: 'guild' }); setT(''); say(P('Đã đăng trong Guild', 'Posted to guild')); } catch (e) { say(errText(e, P)); } }} data-testid="gd-post-go">{P('Đăng', 'Post')}</button></div>
      <small className="muted">{P('Bài Guild không bao giờ hiện ở bảng tin công khai.', 'Guild posts never appear in public feeds.')}</small>
      {feed(w, me, 'guild').filter((p) => p.guildId != null).map((p) => <PostCard key={p.id} h={h} p={p} />)}
    </div>
  );
}

function Manage({ h, g, role }: { h: Hub; g: WGuild; role: Role }) {
  const { P, w, me, say } = h;
  const [uid, setUid] = useState('');
  const [ann, setAnn] = useState('');
  const [confirm, setConfirm] = useState('');
  const act = (f: () => void, ok: string) => { try { f(); say(ok); } catch (e) { say(errText(e, P)); } };
  const mine = roleRank(role);
  return (
    <div className="lp-col">
      {mine >= 3 && <>
        <b>✉️ {P('Mời bằng UID', 'Invite by UID')}</b>
        <div className="sh-row"><input className="lb-input" value={uid} onChange={(e) => setUid(e.target.value.replace(/\D/g, ''))} placeholder="UID" data-testid="gd-invite-uid" /><button className="btn small primary" disabled={!uid} onClick={() => act(() => guildInvite(me, Number(uid)), P('Đã gửi lời mời', 'Invite sent'))} data-testid="gd-invite">{P('Mời', 'Invite')}</button></div>
        {g.requests.length > 0 && <b>{P('Yêu cầu tham gia', 'Join requests')}</b>}
        {g.requests.map((r) => <div key={r} className="lp-friend"><span className="grow">{acc(w, r)?.name}</span><button className="btn small primary" onClick={() => act(() => decideRequest(me, r, true), P('Đã duyệt', 'Accepted'))}>{P('Duyệt', 'Accept')}</button><button className="btn small ghost" onClick={() => act(() => decideRequest(me, r, false), P('Đã từ chối', 'Rejected'))}>{P('Từ chối', 'Reject')}</button></div>)}
        <b>📢 {P('Đăng thông báo', 'Post announcement')}</b>
        <div className="sh-row"><input className="lb-input" maxLength={300} value={ann} onChange={(e) => setAnn(e.target.value)} /><button className="btn small" disabled={!ann.trim()} onClick={() => act(() => { announce(me, ann); setAnn(''); }, P('Đã đăng', 'Posted'))}>{P('Đăng', 'Post')}</button></div>
      </>}
      <b>👥 {P('Vai trò', 'Roles')}</b>
      <small className="muted">{P('Guild Master > Vice > Officer > Elite > Member. Chỉ quản lý được người có vai trò thấp hơn mình; kiểm tra phía server.', 'Guild Master > Vice > Officer > Elite > Member. You can only manage lower ranks; checked on the server.')}</small>
      {g.members.filter((m) => m.uid !== me).map((m) => <div key={m.uid} className="lp-friend"><span className="grow"><b className="ellipsis">{acc(w, m.uid)?.name}</b><small>{roleName(m.role, P)}</small></span>
        {mine > roleRank(m.role) && mine >= 3 && <select className="lb-input small" value={m.role} onChange={(e) => act(() => setRole(me, m.uid, e.target.value as Role), P('Đã đổi vai trò', 'Role changed'))} data-testid={`gd-role-${m.uid}`}>
          {ROLES.filter((r) => r === m.role || (r === 'leader' ? role === 'leader' : roleRank(r) < mine)).map((r) => <option key={r} value={r}>{roleName(r, P)}</option>)}</select>}
        {mine >= 3 && mine > roleRank(m.role) && <button className="btn small ghost" onClick={() => act(() => kick(me, m.uid), P('Đã mời rời', 'Removed'))}>{P('Mời rời', 'Kick')}</button>}</div>)}
      <button className="btn ghost" onClick={() => act(() => leaveGuild(me), P('Đã rời Guild', 'Left the guild'))} data-testid="gd-leave">{P('Rời Guild', 'Leave guild')}</button>
      {role === 'leader' && <div className="lp-card"><b>⚠️ {P('Giải tán Guild', 'Dissolve guild')}</b><small>{P('Chỉ khi chỉ còn mình bạn. Nhập đúng tên Guild để xác nhận. Bài Guild sẽ bị xóa (không lộ ra công khai).', 'Only when you are the last member. Type the guild name to confirm. Guild posts are deleted (never made public).')}</small>
        <div className="sh-row"><input className="lb-input" value={confirm} onChange={(e) => setConfirm(e.target.value)} placeholder={g.name} /><button className="btn small danger" onClick={() => act(() => dissolve(me, confirm), P('Đã giải tán', 'Dissolved'))}>{P('Giải tán', 'Dissolve')}</button></div></div>}
    </div>
  );
}
