const RELOAD_QUERY_PARAMETER = '_gb_reload'

export function markBootstrapReady() {
  window.dispatchEvent(new Event('garagebalance:bootstrap-ready'))

  const currentUrl = new URL(window.location.href)
  if (currentUrl.searchParams.has(RELOAD_QUERY_PARAMETER)) {
    currentUrl.searchParams.delete(RELOAD_QUERY_PARAMETER)
    window.history.replaceState(window.history.state, '', currentUrl)
  }
}

export function getFreshApplicationUrl(locationHref = window.location.href, timestamp = Date.now()) {
  const nextUrl = new URL(locationHref)
  nextUrl.searchParams.set(RELOAD_QUERY_PARAMETER, timestamp.toString())
  return nextUrl.toString()
}

export function reloadApplication() {
  window.location.replace(getFreshApplicationUrl())
}
