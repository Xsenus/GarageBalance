type DebouncedRequestOptions<TResult> = {
  delay?: number
  request: (signal: AbortSignal) => Promise<TResult>
  onStart: () => void
  onSuccess: (result: TResult) => void
  onError: (error: unknown) => void
}

export function scheduleDebouncedRequest<TResult>({ delay = 350, request, onStart, onSuccess, onError }: DebouncedRequestOptions<TResult>) {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => {
    onStart()
    void request(controller.signal).then((result) => {
      if (!controller.signal.aborted) onSuccess(result)
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) onError(error)
    })
  }, delay)

  return () => {
    window.clearTimeout(timeoutId)
    controller.abort()
  }
}
