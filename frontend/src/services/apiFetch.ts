const defaultReadTimeoutMs = 15_000
const defaultWriteTimeoutMs = 60_000

export class ApiRequestTimeoutError extends Error {
  constructor() {
    super('Сервер слишком долго не отвечает. Проверьте подключение и повторите запрос.')
    this.name = 'ApiRequestTimeoutError'
  }
}

function createAbortError(): Error {
  return new DOMException('The operation was aborted.', 'AbortError')
}

async function fetchAttempt(input: RequestInfo | URL, init: RequestInit, timeoutMs: number): Promise<Response> {
  const controller = new AbortController()
  let timedOut = false
  const timeoutId = globalThis.setTimeout(() => {
    timedOut = true
    controller.abort()
  }, timeoutMs)
  const callerSignal = init.signal
  const abortFromCaller = () => controller.abort(callerSignal?.reason)

  if (callerSignal?.aborted) {
    globalThis.clearTimeout(timeoutId)
    throw callerSignal.reason ?? createAbortError()
  }

  callerSignal?.addEventListener('abort', abortFromCaller, { once: true })
  try {
    const requestInit = { ...init }
    Object.defineProperty(requestInit, 'signal', {
      value: controller.signal,
      enumerable: false,
    })
    return await fetch(input, requestInit)
  } catch (error) {
    if (timedOut) {
      throw new ApiRequestTimeoutError()
    }

    throw error
  } finally {
    globalThis.clearTimeout(timeoutId)
    callerSignal?.removeEventListener('abort', abortFromCaller)
  }
}

export async function apiFetch(input: RequestInfo | URL, init: RequestInit = {}): Promise<Response> {
  const method = init.method?.toUpperCase() ?? 'GET'
  const canRetry = method === 'GET' || method === 'HEAD'
  const timeoutMs = canRetry ? defaultReadTimeoutMs : defaultWriteTimeoutMs

  try {
    return await fetchAttempt(input, init, timeoutMs)
  } catch (error) {
    if (!canRetry || init.signal?.aborted || (!(error instanceof TypeError) && !(error instanceof ApiRequestTimeoutError))) {
      throw error
    }
  }

  return fetchAttempt(input, init, timeoutMs)
}
