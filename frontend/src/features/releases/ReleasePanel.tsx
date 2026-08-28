import { useCallback, useEffect, useRef, useState } from 'react'
import { BookOpenCheck } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { AppReleaseDto, ReleaseClient } from '../../services/releasesApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import { formatReleaseDate } from '../../shared/formatters'
import { AsyncErrorState, EmptyState, LoadingSkeleton } from '../../shared/AsyncState'

const releasePageSize = 9

export function ReleasePanel({ auth, releaseClient }: { auth: AuthResponse; releaseClient: ReleaseClient }) {
  const [releases, setReleases] = useState<AppReleaseDto[]>([])
  const [loading, setLoading] = useState(true)
  const [loadingMore, setLoadingMore] = useState(false)
  const [totalCount, setTotalCount] = useState(0)
  const [hasMore, setHasMore] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [loadMoreError, setLoadMoreError] = useState<string | null>(null)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)
  const canManageReleases = hasPermission(auth, permissions.appReleasesManage)
  const loadMoreControllerRef = useRef<AbortController | null>(null)
  const listRevisionRef = useRef(0)
  const getReleasePage = useCallback((offset: number, signal?: AbortSignal) => canManageReleases
    ? releaseClient.getManageableReleases(auth.accessToken, offset, releasePageSize, signal)
    : releaseClient.getReleases(auth.accessToken, offset, releasePageSize, signal), [auth.accessToken, canManageReleases, releaseClient])

  useEffect(() => {
    let ignore = false
    const controller = new AbortController()
    const revision = ++listRevisionRef.current
    loadMoreControllerRef.current?.abort()
    loadMoreControllerRef.current = null

    async function loadReleases() {
      setLoading(true)
      setError(null)

      try {
        const page = await getReleasePage(0, controller.signal)
        if (!ignore && revision === listRevisionRef.current) {
          setReleases(page.items)
          setTotalCount(page.totalCount)
          setHasMore(page.hasMore)
        }
      } catch (caught) {
        if (!ignore && revision === listRevisionRef.current) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить историю обновлений.')
        }
      } finally {
        if (!ignore && revision === listRevisionRef.current) {
          setLoading(false)
        }
      }
    }

    void loadReleases()

    return () => {
      ignore = true
      controller.abort()
    }
  }, [getReleasePage])

  useEffect(() => () => {
    listRevisionRef.current += 1
    loadMoreControllerRef.current?.abort()
    loadMoreControllerRef.current = null
  }, [])

  async function refreshReleases() {
    loadMoreControllerRef.current?.abort()
    const controller = new AbortController()
    loadMoreControllerRef.current = controller
    setLoadingMore(false)
    try {
      const page = await getReleasePage(0, controller.signal)
      if (controller.signal.aborted) {
        return
      }
      setReleases(page.items)
      setTotalCount(page.totalCount)
      setHasMore(page.hasMore)
    } catch (caught) {
      if (controller.signal.aborted) {
        return
      }
      throw caught
    } finally {
      if (loadMoreControllerRef.current === controller) {
        loadMoreControllerRef.current = null
      }
    }
  }

  async function retryInitialLoad() {
    setLoading(true)
    setError(null)
    try {
      await refreshReleases()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось загрузить историю обновлений.')
    } finally {
      setLoading(false)
    }
  }

  const loadMoreReleases = useCallback(async () => {
    if (!hasMore || loadMoreControllerRef.current) {
      return
    }

    const controller = new AbortController()
    const revision = listRevisionRef.current
    loadMoreControllerRef.current = controller
    setLoadingMore(true)
    setLoadMoreError(null)
    try {
      const page = await getReleasePage(releases.length, controller.signal)
      if (controller.signal.aborted || revision !== listRevisionRef.current) {
        return
      }
      setReleases((current) => {
        const knownIds = new Set(current.map((release) => release.releaseId))
        return [...current, ...page.items.filter((release) => !knownIds.has(release.releaseId))]
      })
      setTotalCount(page.totalCount)
      setHasMore(page.hasMore)
    } catch (caught) {
      if (!controller.signal.aborted && revision === listRevisionRef.current) {
        setLoadMoreError(caught instanceof Error ? caught.message : 'Не удалось загрузить следующие новости.')
      }
    } finally {
      if (loadMoreControllerRef.current === controller) {
        loadMoreControllerRef.current = null
        if (!controller.signal.aborted) {
          setLoadingMore(false)
        }
      }
    }
  }, [getReleasePage, hasMore, releases.length])

  const loadMoreSentinelRef = useCallback((node: HTMLDivElement | null) => {
    if (!node || !hasMore || typeof IntersectionObserver === 'undefined') {
      return
    }

    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) {
        void loadMoreReleases()
      }
    }, { rootMargin: '240px 0px' })
    observer.observe(node)
    return () => observer.disconnect()
  }, [hasMore, loadMoreReleases])

  async function publishRelease(release: AppReleaseDto) {
    setSaving(true)
    setError(null)
    setSuccessMessage(null)
    try {
      const publishedRelease = await releaseClient.publishRelease(auth.accessToken, release.releaseId)
      setReleases((current) => current.map((item) => item.releaseId === publishedRelease.releaseId ? publishedRelease : item))
      setSuccessMessage(`Запись ${release.version} опубликована.`)
      setSaving(false)
      void refreshReleases().catch((caught) => {
        setError(caught instanceof Error ? caught.message : 'Запись опубликована, но список не удалось обновить.')
      })
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось опубликовать запись.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="release-panel" aria-label="Что нового">
      <div className="panel-heading">
        <div>
          <p className="eyebrow">Что нового</p>
          <h2>История обновлений</h2>
        </div>
        <div className="release-heading-actions">
          <span>{totalCount} версий</span>
        </div>
      </div>

      {loading ? <LoadingSkeleton className="release-list-skeleton" label="Загружаем историю обновлений" rows={3} columns={3} /> : null}
      {error ? <AsyncErrorState message={error} onRetry={() => void retryInitialLoad()} retrying={loading} /> : null}
      {successMessage ? <p className="success-text" role="status" aria-live="polite">{successMessage}</p> : null}
      {!loading && !error && releases.length === 0 ? <EmptyState>Пока нет опубликованных изменений.</EmptyState> : null}

      {!loading && releases.length > 0 ? (
        <div className="release-list">
          {releases.map((release) => (
            <article className="release-entry" key={release.releaseId}>
              <div className="release-entry__header">
                <div>
                  <h3>{release.title}</h3>
                  <p>{release.summary}</p>
                </div>
                <div className="release-entry__meta">
                  <span>
                    v{release.version} · {formatReleaseDate(release.publishedAt)}
                  </span>
                  {canManageReleases && release.isPublished === false ? <strong>Черновик</strong> : null}
                </div>
              </div>
              <ul>
                {release.items.map((item) => (
                  <li className={`release-item release-item--${item.type}`} key={`${release.releaseId}-${item.type}-${item.text}`}>
                    {item.text}
                  </li>
                ))}
              </ul>
              {canManageReleases && release.isPublished === false ? (
                <div className="inline-actions release-entry__actions">
                  <button className="secondary-button" type="button" onClick={() => void publishRelease(release)} disabled={saving}>
                    <BookOpenCheck size={16} />
                    <span>Опубликовать</span>
                  </button>
                </div>
              ) : null}
            </article>
          ))}
        </div>
      ) : null}
      {!loading && !error && hasMore ? (
        <div className="release-load-more" ref={loadMoreSentinelRef} role="status" aria-live="polite">
          {loadingMore ? 'Загружаем следующие новости…' : 'Прокрутите ниже, чтобы увидеть более ранние новости'}
        </div>
      ) : null}
      {loadMoreError ? (
        <AsyncErrorState className="release-load-more-error" message={loadMoreError} onRetry={() => void loadMoreReleases()} retrying={loadingMore} />
      ) : null}
    </section>
  )
}
