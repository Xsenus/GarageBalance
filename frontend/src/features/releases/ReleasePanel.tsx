import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { BookOpenCheck, Pencil, Plus, Save } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { AppReleaseDto, AppReleaseItemDto, ReleaseClient, UpsertAppReleaseRequest } from '../../services/releasesApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import { FormError } from '../../shared/formFeedback'
import { formatReleaseDate } from '../../shared/formatters'

export function ReleasePanel({ auth, releaseClient }: { auth: AuthResponse; releaseClient: ReleaseClient }) {
  const [releases, setReleases] = useState<AppReleaseDto[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)
  const [editor, setEditor] = useState<ReleaseEditorState | null>(null)
  const canManageReleases = hasPermission(auth, permissions.appReleasesManage)

  useEffect(() => {
    let ignore = false

    async function loadReleases() {
      setLoading(true)
      setError(null)

      try {
        const nextReleases = canManageReleases
          ? await releaseClient.getManageableReleases(auth.accessToken, 50)
          : await releaseClient.getReleases(auth.accessToken, 10)
        if (!ignore) {
          setReleases(nextReleases)
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить историю обновлений.')
        }
      } finally {
        if (!ignore) {
          setLoading(false)
        }
      }
    }

    void loadReleases()

    return () => {
      ignore = true
    }
  }, [auth.accessToken, canManageReleases, releaseClient])

  async function refreshReleases() {
    const nextReleases = canManageReleases
      ? await releaseClient.getManageableReleases(auth.accessToken, 50)
      : await releaseClient.getReleases(auth.accessToken, 10)
    setReleases(nextReleases)
  }

  async function saveRelease(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!editor) {
      return
    }

    const items = editor.items
      .map((item) => ({ type: item.type, text: item.text.trim() }))
      .filter((item) => item.text.length > 0)
    if (!editor.version.trim() || !editor.title.trim() || !editor.summary.trim() || items.length === 0) {
      setError('Заполните версию, заголовок, описание и хотя бы один пункт.')
      return
    }

    setSaving(true)
    setError(null)
    setSuccessMessage(null)
    try {
      const request: UpsertAppReleaseRequest = {
        releaseId: editor.releaseId,
        version: editor.version.trim(),
        publishedAt: editor.publishedAt || null,
        title: editor.title.trim(),
        summary: editor.summary.trim(),
        items,
        isPublished: editor.isPublished,
      }
      if (editor.mode === 'create') {
        await releaseClient.createRelease(auth.accessToken, request)
        setSuccessMessage(editor.isPublished ? 'Запись добавлена и опубликована.' : 'Черновик добавлен.')
      } else {
        await releaseClient.updateRelease(auth.accessToken, editor.releaseId, request)
        setSuccessMessage(editor.isPublished ? 'Запись обновлена.' : 'Черновик обновлен.')
      }

      setEditor(null)
      await refreshReleases()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось сохранить запись.')
    } finally {
      setSaving(false)
    }
  }

  async function publishRelease(release: AppReleaseDto) {
    setSaving(true)
    setError(null)
    setSuccessMessage(null)
    try {
      await releaseClient.publishRelease(auth.accessToken, release.releaseId)
      setSuccessMessage(`Запись ${release.version} опубликована.`)
      await refreshReleases()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось опубликовать запись.')
    } finally {
      setSaving(false)
    }
  }

  function openCreateEditor() {
    setEditor(createReleaseEditorState())
    setError(null)
    setSuccessMessage(null)
  }

  function openEditEditor(release: AppReleaseDto) {
    setEditor(createReleaseEditorState(release))
    setError(null)
    setSuccessMessage(null)
  }

  function updateEditorItem(index: number, patch: Partial<AppReleaseItemDto>) {
    setEditor((current) => {
      if (!current) {
        return current
      }

      return {
        ...current,
        items: current.items.map((item, itemIndex) => itemIndex === index ? { ...item, ...patch } : item),
      }
    })
  }

  function addEditorItem() {
    setEditor((current) => current
      ? { ...current, items: [...current.items, { type: 'improved', text: '' }] }
      : current)
  }

  function removeEditorItem(index: number) {
    setEditor((current) => {
      if (!current || current.items.length === 1) {
        return current
      }

      return {
        ...current,
        items: current.items.filter((_, itemIndex) => itemIndex !== index),
      }
    })
  }

  return (
    <section className="release-panel" aria-label="Что нового">
      <div className="panel-heading">
        <div>
          <p className="eyebrow">Что нового</p>
          <h2>История обновлений</h2>
        </div>
        <div className="release-heading-actions">
          <span>{releases.length} версий</span>
          {canManageReleases ? (
            <button className="secondary-button" type="button" onClick={openCreateEditor}>
              <Plus size={17} />
              <span>Добавить запись</span>
            </button>
          ) : null}
        </div>
      </div>

      {loading ? <p className="muted" role="status" aria-live="polite">Загружаем историю обновлений...</p> : null}
      {error ? <FormError>{error}</FormError> : null}
      {successMessage ? <p className="success-text" role="status" aria-live="polite">{successMessage}</p> : null}
      {editor ? (
        <form className="release-editor" aria-label={editor.mode === 'create' ? 'Новая запись Что нового' : `Редактирование ${editor.version}`} onSubmit={saveRelease}>
          <div className="form-grid two-columns">
            <label>
              Версия
              <input aria-label="Версия записи Что нового" value={editor.version} onChange={(event) => setEditor({ ...editor, version: event.target.value })} />
            </label>
            <label>
              Дата публикации
              <input aria-label="Дата публикации записи Что нового" type="datetime-local" value={editor.publishedAt} onChange={(event) => setEditor({ ...editor, publishedAt: event.target.value })} />
            </label>
          </div>
          <label>
            Заголовок
            <input aria-label="Заголовок записи Что нового" value={editor.title} onChange={(event) => setEditor({ ...editor, title: event.target.value })} />
          </label>
          <label>
            Краткое описание
            <textarea aria-label="Краткое описание записи Что нового" rows={3} value={editor.summary} onChange={(event) => setEditor({ ...editor, summary: event.target.value })} />
          </label>
          <div className="release-editor__items">
            <div className="release-editor__items-heading">
              <strong>Пункты обновления</strong>
              <button className="ghost-button" type="button" onClick={addEditorItem}>Добавить пункт</button>
            </div>
            {editor.items.map((item, index) => (
              <div className="release-editor__item" key={`${index}-${item.type}`}>
                <label>
                  Тип
                  <select aria-label={`Тип пункта обновления ${index + 1}`} value={item.type} onChange={(event) => updateEditorItem(index, { type: event.target.value })}>
                    {releaseItemTypeOptions.map((option) => (
                      <option value={option.value} key={option.value}>{option.label}</option>
                    ))}
                  </select>
                </label>
                <label>
                  Текст
                  <textarea aria-label={`Текст пункта обновления ${index + 1}`} rows={2} value={item.text} onChange={(event) => updateEditorItem(index, { text: event.target.value })} />
                </label>
                <button className="ghost-button" type="button" onClick={() => removeEditorItem(index)} disabled={editor.items.length === 1}>Убрать</button>
              </div>
            ))}
          </div>
          <label className="checkbox-row">
            <input aria-label="Опубликовать запись Что нового сразу" type="checkbox" checked={editor.isPublished} onChange={(event) => setEditor({ ...editor, isPublished: event.target.checked })} />
            <span>Опубликовать сразу</span>
          </label>
          <div className="detail-dialog-actions">
            <button className="ghost-button" type="button" onClick={() => setEditor(null)} disabled={saving}>Отмена</button>
            <button className="secondary-button" type="submit" disabled={saving}>
              <Save size={17} />
              <span>{saving ? 'Сохраняем...' : 'Сохранить'}</span>
            </button>
          </div>
        </form>
      ) : null}
      {!loading && !error && releases.length === 0 ? <p className="muted" role="status" aria-live="polite">Пока нет опубликованных изменений.</p> : null}

      {!loading && !error && releases.length > 0 ? (
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
              {canManageReleases ? (
                <div className="inline-actions release-entry__actions">
                  <button className="ghost-button" type="button" onClick={() => openEditEditor(release)} disabled={saving}>
                    <Pencil size={16} />
                    <span>Изменить</span>
                  </button>
                  {release.isPublished === false ? (
                    <button className="secondary-button" type="button" onClick={() => void publishRelease(release)} disabled={saving}>
                      <BookOpenCheck size={16} />
                      <span>Опубликовать</span>
                    </button>
                  ) : null}
                </div>
              ) : null}
            </article>
          ))}
        </div>
      ) : null}
    </section>
  )
}

type ReleaseEditorState = {
  mode: 'create' | 'edit'
  releaseId: string
  version: string
  publishedAt: string
  title: string
  summary: string
  items: AppReleaseItemDto[]
  isPublished: boolean
}

const releaseItemTypeOptions = [
  { value: 'new', label: 'Новое' },
  { value: 'improved', label: 'Улучшение' },
  { value: 'fixed', label: 'Исправление' },
  { value: 'important', label: 'Важно' },
]

function createReleaseEditorState(release?: AppReleaseDto): ReleaseEditorState {
  return {
    mode: release ? 'edit' : 'create',
    releaseId: release?.releaseId ?? '',
    version: release?.version ?? '',
    publishedAt: release ? formatDateTimeInputValue(release.publishedAt) : formatDateTimeInputValue(new Date().toISOString()),
    title: release?.title ?? '',
    summary: release?.summary ?? '',
    items: release?.items.length ? release.items.map((item) => ({ ...item })) : [{ type: 'improved', text: '' }],
    isPublished: release ? release.isPublished !== false : false,
  }
}

function formatDateTimeInputValue(value: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return ''
  }

  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  const hours = String(date.getHours()).padStart(2, '0')
  const minutes = String(date.getMinutes()).padStart(2, '0')
  return `${year}-${month}-${day}T${hours}:${minutes}`
}
