import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import App from './App';
import { I18nProvider } from './i18n';
import { CatalogProvider } from './state/catalog';
import { SessionProvider } from './state/session';
import { RealtimeProvider } from './state/realtime';
import { ToastProvider } from './components/ui';
import { ViewportProvider } from './state/viewport';
import { PwaProvider } from './state/pwa';
import './styles.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ViewportProvider>
    <PwaProvider>
    <I18nProvider>
      <CatalogProvider>
        <ToastProvider>
          <SessionProvider>
            <RealtimeProvider>
              <BrowserRouter>
                <App />
              </BrowserRouter>
            </RealtimeProvider>
          </SessionProvider>
        </ToastProvider>
      </CatalogProvider>
    </I18nProvider>
    </PwaProvider>
    </ViewportProvider>
  </StrictMode>,
);
