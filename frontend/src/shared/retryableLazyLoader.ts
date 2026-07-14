export function createRetryableLazyLoader<T>(loader: () => Promise<T>): () => Promise<T> {
  let cachedRequest: Promise<T> | null = null

  return () => {
    if (cachedRequest) return cachedRequest

    let loaderRequest: Promise<T>
    try {
      loaderRequest = loader()
    } catch (error) {
      return Promise.reject(error)
    }

    const retryableRequest = loaderRequest.catch((error: unknown) => {
      if (cachedRequest === retryableRequest) cachedRequest = null
      throw error
    })
    cachedRequest = retryableRequest
    return retryableRequest
  }
}
