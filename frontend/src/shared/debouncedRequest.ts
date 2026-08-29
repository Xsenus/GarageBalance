type DebouncedRequestOptions<TResult> = {
  delay?: number
  requestTimeout?: number
  timeoutError?: unknown
  request: (signal: AbortSignal) => Promise<TResult>
  onStart: () => void
  onSuccess: (result: TResult) => void
  onError: (error: unknown) => void
}

export function scheduleDebouncedRequest<TResult>({ delay = 350, requestTimeout, timeoutError, request, onStart, onSuccess, onError }: DebouncedRequestOptions<TResult>) {
  const controller = new AbortController()
  let requestTimeoutId = 0
  let settled = false
  const finish = (callback: () => void) => {
    if (settled || controller.signal.aborted) return
    settled = true
    window.clearTimeout(requestTimeoutId)
    callback()
  }
  const timeoutId = window.setTimeout(() => {
    onStart()
    let pendingRequest: Promise<TResult>
    try {
      pendingRequest = request(controller.signal)
    } catch (error: unknown) {
      finish(() => onError(error))
      return
    }
    if (requestTimeout !== undefined) {
      requestTimeoutId = window.setTimeout(() => {
        if (settled || controller.signal.aborted) return
        settled = true
        controller.abort()
        onError(timeoutError)
      }, requestTimeout)
    }
    void pendingRequest.then((result) => finish(() => onSuccess(result)))
      .catch((error: unknown) => finish(() => onError(error)))
  }, delay)

  return () => {
    window.clearTimeout(timeoutId)
    window.clearTimeout(requestTimeoutId)
    settled = true
    controller.abort()
  }
}
