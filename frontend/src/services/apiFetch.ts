const defaultReadTimeoutMs = 15_000
const defaultWriteTimeoutMs = 60_000
const inFlightSafeRequests = new Map<string, Promise<Response>>()

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

function getRequestMethod(input: RequestInfo | URL, init: RequestInit): string {
  if (init.method) {
    return init.method.toUpperCase()
  }

  return input instanceof Request ? input.method.toUpperCase() : 'GET'
}

function createInFlightRequestKey(input: RequestInfo | URL, init: RequestInit, method: string): string | null {
  if ((method !== 'GET' && method !== 'HEAD') || init.signal || init.body) {
    return null
  }

  if (typeof input !== 'string' && !(input instanceof URL)) {
    return null
  }

  const headers = [...new Headers(init.headers).entries()]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([name, value]) => `${name}:${value}`)
    .join('\n')

  return [
    method,
    input.toString(),
    init.credentials ?? '',
    init.mode ?? '',
    headers,
  ].join('\n')
}

async function fetchWithRetry(input: RequestInfo | URL, init: RequestInit, method: string): Promise<Response> {
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

export async function apiFetch(input: RequestInfo | URL, init: RequestInit = {}): Promise<Response> {
  const method = getRequestMethod(input, init)
  const requestKey = createInFlightRequestKey(input, init, method)
  if (!requestKey) {
    return fetchWithRetry(input, init, method)
  }

  const inFlightRequest = inFlightSafeRequests.get(requestKey)
  if (inFlightRequest) {
    return (await inFlightRequest).clone()
  }

  const request = fetchWithRetry(input, init, method)
  inFlightSafeRequests.set(requestKey, request)
  try {
    return await request
  } finally {
    if (inFlightSafeRequests.get(requestKey) === request) {
      inFlightSafeRequests.delete(requestKey)
    }
  }
}
