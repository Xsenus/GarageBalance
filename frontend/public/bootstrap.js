(() => {
  const root = document.getElementById('root')
  let bootstrapFinished = false

  const showBootstrapError = () => {
    if (bootstrapFinished || !root) return
    bootstrapFinished = true
    window.clearTimeout(bootstrapTimeout)

    const loader = document.createElement('div')
    loader.className = 'app-bootstrap-loader'
    loader.setAttribute('role', 'alert')

    const card = document.createElement('div')
    card.className = 'app-bootstrap-loader__card'

    const message = document.createElement('p')
    message.className = 'app-bootstrap-loader__message'
    message.textContent = 'Не удалось загрузить GarageBalance'

    const hint = document.createElement('p')
    hint.className = 'app-bootstrap-loader__hint'
    hint.textContent = 'Проверьте подключение к интернету и повторите загрузку страницы.'

    const retry = document.createElement('button')
    retry.className = 'app-bootstrap-loader__retry'
    retry.type = 'button'
    retry.textContent = 'Повторить загрузку'
    retry.addEventListener('click', () => {
      const nextUrl = new URL(window.location.href)
      nextUrl.searchParams.set('_gb_reload', Date.now().toString())
      window.location.replace(nextUrl)
    })

    card.append(message, hint, retry)
    loader.append(card)
    root.replaceChildren(loader)
  }

  const bootstrapTimeout = window.setTimeout(showBootstrapError, 20000)

  window.addEventListener('garagebalance:bootstrap-ready', () => {
    bootstrapFinished = true
    window.clearTimeout(bootstrapTimeout)
  }, { once: true })

  window.addEventListener('error', (event) => {
    if (!bootstrapFinished) {
      console.error('GarageBalance bootstrap failed.', event.error ?? event.message)
      showBootstrapError()
    }
  }, true)

  window.addEventListener('unhandledrejection', (event) => {
    if (!bootstrapFinished) {
      console.error('GarageBalance bootstrap promise failed.', event.reason)
      showBootstrapError()
    }
  }, { once: true })
})()
