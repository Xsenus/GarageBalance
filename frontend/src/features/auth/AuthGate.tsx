import { useState } from 'react'
import type { FormEvent } from 'react'
import type { AuthClient, AuthResponse } from '../../services/authApi'
import { FormError, FormValidationSummary } from '../../shared/formFeedback'
import { getAuthValidationErrors } from '../../shared/validation'

type AuthGateProps = {
  authClient: AuthClient
  onAuthenticated: (auth: AuthResponse) => void
}

export function AuthGate({ authClient, onAuthenticated }: AuthGateProps) {
  const [email, setEmail] = useState('admin@example.com')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [validationErrors, setValidationErrors] = useState<string[]>([])
  const [loading, setLoading] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    const errors = getAuthValidationErrors('login', email, '', password)
    if (errors.length > 0) {
      setValidationErrors(errors)
      return
    }

    setValidationErrors([])
    setLoading(true)

    try {
      const response = await authClient.login({ email, password })
      setPassword('')
      onAuthenticated(response)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось выполнить вход.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <section className="auth-layout" aria-label="Вход в систему">
      <form className="auth-card" onSubmit={handleSubmit}>
        <div className="auth-card-header">
          <h1>Авторизация</h1>
        </div>

        <label>
          Email
          <input aria-label="Email" value={email} onChange={(event) => setEmail(event.target.value)} type="email" autoComplete="username" placeholder="admin@example.com" required />
        </label>

        <label>
          Пароль
          <input aria-label="Пароль" value={password} onChange={(event) => setPassword(event.target.value)} type="password" autoComplete="current-password" minLength={8} required />
        </label>

        <FormValidationSummary title="Проверьте форму входа" items={validationErrors} />
        {error ? <FormError>{error}</FormError> : null}

        <button className="primary-button" type="submit" disabled={loading}>
          {loading ? 'Проверяем...' : 'Войти'}
        </button>
      </form>
    </section>
  )
}
