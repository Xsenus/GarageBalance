import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { AppBootstrapBoundary, AppBootstrapReady } from './AppBootstrap.tsx'

const root = createRoot(document.getElementById('root')!)

root.render(
  <StrictMode>
    <AppBootstrapBoundary>
      <AppBootstrapReady>
        <App />
      </AppBootstrapReady>
    </AppBootstrapBoundary>
  </StrictMode>,
)
