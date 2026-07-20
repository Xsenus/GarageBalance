export const fundsLoadAttemptTimeoutMs = 8_000

class FundsLoadTimeoutError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'FundsLoadTimeoutError'
  }
}

function createAbortError() {
  return new DOMException('Загрузка фондов отменена.', 'AbortError')
}

function withTimeout<T>(requestFactory: (signal: AbortSignal) => Promise<T>, timeoutMessage: string, cancellationSignal?: AbortSignal): Promise<T> {
  const controller = new AbortController()
  let timeoutHandle = 0
  let cancellationHandler: (() => void) | undefined
  const timeout = new Promise<never>((_resolve, reject) => {
    timeoutHandle = window.setTimeout(() => {
      controller.abort()
      reject(new FundsLoadTimeoutError(timeoutMessage))
    }, fundsLoadAttemptTimeoutMs)
  })
  const cancellation = new Promise<never>((_resolve, reject) => {
    if (cancellationSignal?.aborted) {
      controller.abort()
      reject(createAbortError())
      return
    }

    cancellationHandler = () => {
      controller.abort()
      reject(createAbortError())
    }
    cancellationSignal?.addEventListener('abort', cancellationHandler, { once: true })
  })

  const request = Promise.resolve().then(() => requestFactory(controller.signal))
  return Promise.race([request, timeout, cancellation]).finally(() => {
    window.clearTimeout(timeoutHandle)
    if (cancellationHandler) {
      cancellationSignal?.removeEventListener('abort', cancellationHandler)
    }
  })
}

export async function loadFundsRequest<T>(requestFactory: (signal: AbortSignal) => Promise<T>, timeoutMessage: string, cancellationSignal?: AbortSignal): Promise<T> {
  try {
    return await withTimeout(requestFactory, timeoutMessage, cancellationSignal)
  } catch (error: unknown) {
    if (!(error instanceof FundsLoadTimeoutError)) {
      throw error
    }
  }

  return withTimeout(requestFactory, timeoutMessage, cancellationSignal)
}
