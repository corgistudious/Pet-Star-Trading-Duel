// Standalone Interactive Web Preview entry (preview.html). No session, realtime or API providers: everything is
// simulated in the browser (see ./sim.ts). Built separately with `npm run build:preview` (relative base './').
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { I18nProvider } from '../i18n';
import { CatalogProvider } from '../state/catalog';
import PreviewApp from './PreviewApp';
import '../styles.css';
import './preview.css';
import './social.css';

// Be Vietnam Pro (SIL OFL 1.1, public/fonts) for crisp Vietnamese diacritics; loaded relative to the preview base so it
// works on any host. font-display: swap keeps text readable while the font loads.
const fontCss = ['Regular:400', 'SemiBold:600', 'Bold:700', 'ExtraBold:800'].map((x) => { const [n, w] = x.split(':');
  return `@font-face{font-family:'Be Vietnam Pro';font-style:normal;font-weight:${w};font-display:swap;src:url('${import.meta.env.BASE_URL}fonts/BeVietnamPro-${n}.woff2') format('woff2');}`; }).join('');
const fontTag = document.createElement('style'); fontTag.textContent = fontCss; document.head.appendChild(fontTag);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nProvider>
      <CatalogProvider>
        <PreviewApp />
      </CatalogProvider>
    </I18nProvider>
  </StrictMode>,
);
