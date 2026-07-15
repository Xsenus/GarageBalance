import { useState } from 'react'
import { authApi } from './services/authApi'
import type { AuthClient, AuthResponse, CurrentUserDto } from './services/authApi'
import { AuthGate } from './features/auth/AuthGate'
import { AuthenticatedAppShell } from './features/workspace/AppShell'
import { auditApi } from './services/auditApi'
import type { AuditClient } from './services/auditApi'
import { clearDictionaryResponseCache, dictionariesApi } from './services/dictionariesApi'
import type { DictionaryClient } from './services/dictionariesApi'
import { financeApi } from './services/financeApi'
import type { FinanceClient } from './services/financeApi'
import { fundsApi } from './services/fundsApi'
import type { FundsClient } from './services/fundsApi'
import { formStatesApi } from './services/formStatesApi'
import type { FormStateClient } from './services/formStatesApi'
import { importApi } from './services/importApi'
import type { ImportClient } from './services/importApi'
import { integrationsApi } from './services/integrationsApi'
import type { IntegrationClient } from './services/integrationsApi'
import { reportsApi } from './services/reportsApi'
import type { ReportClient } from './services/reportsApi'
import { releasesApi } from './services/releasesApi'
import type { ReleaseClient } from './services/releasesApi'
import { usersApi } from './services/usersApi'
import type { UserManagementClient } from './services/usersApi'
import { settingsApi } from './services/settingsApi'
import type { ApplicationSettingsClient } from './services/settingsApi'
import { clearStoredAuthSession, loadStoredAuthSession, saveStoredAuthSession } from './shared/sessionStorage'
import { useClientErrorReporting } from './shared/useClientErrorReporting'
import './App.css'

type AppProps = {
  authClient?: AuthClient
  auditClient?: AuditClient
  dictionaryClient?: DictionaryClient
  financeClient?: FinanceClient
  fundsClient?: FundsClient
  formStateClient?: FormStateClient
  importClient?: ImportClient
  integrationClient?: IntegrationClient
  reportClient?: ReportClient
  releaseClient?: ReleaseClient
  userClient?: UserManagementClient
  settingsClient?: ApplicationSettingsClient
}

const authSessionStorageKey = 'garagebalance.auth.session'

function App({ authClient = authApi, auditClient = auditApi, dictionaryClient = dictionariesApi, financeClient = financeApi, fundsClient = fundsApi, formStateClient = formStatesApi, importClient = importApi, integrationClient = integrationsApi, reportClient = reportsApi, releaseClient = releasesApi, settingsClient = settingsApi, userClient = usersApi }: AppProps) {
  const [auth, setAuth] = useState<AuthResponse | null>(() => loadStoredAuthSession(authSessionStorageKey))
  useClientErrorReporting(auth?.accessToken ?? null)

  function handleAuthenticated(nextAuth: AuthResponse) {
    saveStoredAuthSession(authSessionStorageKey, nextAuth)
    setAuth(nextAuth)
  }

  function handleUserChanged(user: CurrentUserDto) {
    setAuth((current) => {
      if (!current) {
        return current
      }

      const nextAuth = { ...current, user }
      saveStoredAuthSession(authSessionStorageKey, nextAuth)
      return nextAuth
    })
  }

  function handleLogout() {
    clearDictionaryResponseCache()
    clearStoredAuthSession(authSessionStorageKey)
    setAuth(null)
  }

  if (!auth) {
    return (
      <main className="auth-entry">
        <AuthGate authClient={authClient} onAuthenticated={handleAuthenticated} />
      </main>
    )
  }

  return (
    <AuthenticatedAppShell auth={auth} authClient={authClient} auditClient={auditClient} dictionaryClient={dictionaryClient} financeClient={financeClient} fundsClient={fundsClient} formStateClient={formStateClient} importClient={importClient} integrationClient={integrationClient} reportClient={reportClient} releaseClient={releaseClient} settingsClient={settingsClient} userClient={userClient} onUserChanged={handleUserChanged} onLogout={handleLogout} />
  )
}

export default App
