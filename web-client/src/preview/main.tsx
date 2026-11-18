// Standalone Interactive Web Preview entry (preview.html). No session, realtime or API providers: everything is
// simulated in the browser (see ./sim.ts). Built separately with `npm run build:preview` (relative base './').
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { I18nProvider } from '../i18n';
import { CatalogProvider } from '../state/catalog';
import PreviewApp from './PreviewApp';
import '../styles.css';
import './preview.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nProvider>
      <CatalogProvider>
        <PreviewApp />
      </CatalogProvider>
    </I18nProvider>
  </StrictMode>,
);
