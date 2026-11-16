import { useState, type FormEvent } from 'react';
import { api } from '../api/client';
import type { AuthResponse } from '../api/types';
import { useI18n } from '../i18n';
import { useSession } from '../state/session';
import { useAction, useToast } from '../components/ui';
import { uiArt } from '../state/catalog';

type Mode = 'login' | 'register' | 'forgot' | 'reset';

export default function AuthScreen() {
  const { t, lang, setLang } = useI18n();
  const { login } = useSession();
  const toast = useToast();
  const { busy, run } = useAction();
  const resetToken = new URLSearchParams(window.location.search).get('reset');
  const [mode, setMode] = useState<Mode>(resetToken ? 'reset' : 'login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [name, setName] = useState('');

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (mode === 'login') {
      const a = await run(() => api.post<AuthResponse>('/api/auth/login', { email, password, platform: 'web', deviceName: navigator.userAgent.slice(0, 60) }, { auth: false }));
      if (a) await login(a);
    } else if (mode === 'register') {
      const a = await run(() => api.post<AuthResponse>('/api/auth/register', { email, password, displayName: name, language: lang }, { auth: false }));
      if (a) await login(a);
    } else if (mode === 'forgot') {
      const ok = await run(() => api.post('/api/auth/forgot-password', { email }, { auth: false }));
      if (ok !== undefined) { toast(t('auth.reset_sent'), 'ok'); setMode('login'); }
    } else {
      const ok = await run(() => api.post('/api/auth/reset-password', { token: resetToken, newPassword: password }, { auth: false }));
      if (ok !== undefined) {
        toast(t('web.reset_done'), 'ok');
        // all sessions were revoked by the reset: start clean on this browser too
        window.location.replace('/');
      }
    }
  };

  return (
    <div className="auth" style={{ backgroundImage: `url(${uiArt('bg_lobby_portrait')})` }}>
      <div className="auth-card">
        <h1 className="logo">Pet Star<small>Trading Duel</small></h1>
        <form onSubmit={submit} className="form">
          {mode !== 'reset' && (
            <label>{t('auth.email')}<input type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} /></label>
          )}
          {mode === 'register' && (
            <label>{t('auth.display_name')}<input required minLength={3} maxLength={20} value={name} onChange={(e) => setName(e.target.value)} /></label>
          )}
          {mode !== 'forgot' && (
            <label>{t(mode === 'login' ? 'auth.password' : 'auth.password_rules')}<input type="password" required minLength={8} autoComplete={mode === 'login' ? 'current-password' : 'new-password'} value={password} onChange={(e) => setPassword(e.target.value)} /></label>
          )}
          <button className="btn primary big" disabled={busy} type="submit">
            {mode === 'login' ? t('auth.login') : mode === 'register' ? t('auth.register') : mode === 'forgot' ? t('web.send_reset') : t('web.set_password')}
          </button>
        </form>
        <div className="auth-links">
          {mode !== 'login' && <button className="link" onClick={() => setMode('login')}>{t('auth.login')}</button>}
          {mode !== 'register' && <button className="link" onClick={() => setMode('register')}>{t('auth.register')}</button>}
          {mode === 'login' && <button className="link" onClick={() => setMode('forgot')}>{t('auth.forgot')}</button>}
        </div>
        <div className="lang-switch">
          <button className={lang === 'en' ? 'on' : ''} onClick={() => setLang('en')}>English</button>
          <button className={lang === 'vi' ? 'on' : ''} onClick={() => setLang('vi')}>Tiếng Việt</button>
        </div>
        <p className="fine">{t('web.no_real_money')}</p>
      </div>
    </div>
  );
}
