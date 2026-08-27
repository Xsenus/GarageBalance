import { lazy, Suspense, useState } from 'react'
import { authApi } from './services/authApi'
import type { AuthClient, AuthResponse } from './services/authApi'
import { AuthGate } from './features/auth/AuthGate'
import type { AuditClient } from './services/auditApi'
import { clearDictionaryResponseCache } from './services/dictionariesApi'
import type { DictionaryClient } from './services/dictionariesApi'
import type { FinanceClient } from './services/financeApi'
import type { FundsClient } from './services/fundsApi'
import type { ImportClient } from './services/importApi'
import type { IntegrationClient } from './services/integrationsApi'
import type { ReportClient } from './services/reportsApi'
import type { ReleaseClient } from './services/releasesApi'
import type { UserManagementClient } from './services/usersApi'
import type { ApplicationSettingsClient } from './services/settingsApi'
import { LoadingSkeleton } from './shared/AsyncState'
import { createRetryableLazyLoader } from './shared/retryableLazyLoader'
import { clearStoredAuthSession, loadStoredAuthSession, saveStoredAuthSession } from './shared/sessionStorage'
import { useClientErrorReporting } from './shared/useClientErrorReporting'
import { useSessionExpiration } from './shared/useSessionExpiration'
import './App.css'

type AppProps = {
  authClient?: AuthClient
  auditClient?: AuditClient
  dictionaryClient?: DictionaryClient
  financeClient?: FinanceClient
  fundsClient?: FundsClient
  importClient?: ImportClient
  integrationClient?: IntegrationClient
  reportClient?: ReportClient
  releaseClient?: ReleaseClient
  userClient?: UserManagementClient
  settingsClient?: ApplicationSettingsClient
}

const authSessionStorageKey = 'garagebalance.auth.session'
const loadAuthenticatedAppShell = createRetryableLazyLoader(() => import('./features/workspace/AppShell').then((module) => ({ default: module.AuthenticatedAppShell })))
const AuthenticatedAppShell = lazy(loadAuthenticatedAppShell)

function App({ authClient = authApi, auditClient, dictionaryClient, financeClient, fundsClient, importClient, integrationClient, reportClient, releaseClient, settingsClient, userClient }: AppProps) {
  const [auth, setAuth] = useState<AuthResponse | null>(() => loadStoredAuthSession(authSessionStorageKey))
  useClientErrorReporting(auth?.accessToken ?? null)

  function handleAuthenticated(nextAuth: AuthResponse) {
    saveStoredAuthSession(authSessionStorageKey, nextAuth)
    setAuth(nextAuth)
  }

  function handleLogout() {
    clearDictionaryResponseCache()
    clearStoredAuthSession(authSessionStorageKey)
    setAuth(null)
  }

  useSessionExpiration(auth?.expiresAtUtc, handleLogout)

  if (!auth) {
    return (
      <main className="auth-entry">
        <AuthGate authClient={authClient} onAuthenticated={handleAuthenticated} />
      </main>
    )
  }

  return (
    <Suspense fallback={<main className="auth-entry"><LoadingSkeleton label="Загружаем рабочее пространство" rows={5} columns={2} /></main>}>
      <AuthenticatedAppShell auth={auth} authClient={authClient} auditClient={auditClient} dictionaryClient={dictionaryClient} financeClient={financeClient} fundsClient={fundsClient} importClient={importClient} integrationClient={integrationClient} reportClient={reportClient} releaseClient={releaseClient} settingsClient={settingsClient} userClient={userClient} onLogout={handleLogout} />
    </Suspense>
  )
}

export default App
