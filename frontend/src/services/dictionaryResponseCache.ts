const dictionaryResponseCacheLifetimeMs = 60_000

type DictionaryCacheEntry = {
  accessToken: string
  expiresAt: number
  response: Promise<unknown>
  tag: string
}

export type DictionaryCacheContext<TResponse> = {
  cacheKey: string
  cacheTag: string | null
  cachedResponse: Promise<TResponse> | null
}

const responseCache = new Map<string, DictionaryCacheEntry>()
const cacheVersions = new Map<string, number>()

const cacheDependencies: Record<string, string[]> = {
  owners: ['owners', 'garages'],
  garages: ['garages'],
  'supplier-groups': ['supplier-groups', 'suppliers'],
  suppliers: ['suppliers', 'supplier-contacts'],
  'supplier-contacts': ['supplier-contacts'],
  'staff-departments': ['staff-departments', 'staff-members'],
  'staff-members': ['staff-members', 'staff-departments'],
  'income-types': ['income-types', 'charge-services', 'fee-campaigns'],
  'expense-types': ['expense-types', 'charge-services', 'suppliers'],
  'measurement-units': ['measurement-units', 'charge-services'],
  tariffs: ['tariffs', 'charge-services'],
  'charge-services': ['charge-services', 'tariffs', 'suppliers', 'measurement-units'],
  'fee-campaigns': ['fee-campaigns'],
  'irregular-payments': ['irregular-payments'],
}

export function clearDictionaryResponseCache() {
  responseCache.clear()
  cacheVersions.clear()
}

function getCacheTag(path: string): string | null {
  return /^\/api\/dictionaries\/([^/?]+)/.exec(path)?.[1] ?? null
}

function getCacheVersion(accessToken: string, tag: string): number {
  return cacheVersions.get(`${accessToken}\n${tag}`) ?? 0
}

export function getDictionaryCacheContext<TResponse>(
  accessToken: string,
  path: string,
  readCachedResponse: boolean,
): DictionaryCacheContext<TResponse> {
  const cacheTag = getCacheTag(path)
  const cacheVersion = cacheTag ? getCacheVersion(accessToken, cacheTag) : 0
  const cacheKey = `${accessToken}\n${cacheTag ?? ''}\n${cacheVersion}\n${path}`
  const cached = readCachedResponse ? responseCache.get(cacheKey) : undefined

  if (cached && cached.expiresAt > Date.now()) {
    return { cacheKey, cacheTag, cachedResponse: cached.response as Promise<TResponse> }
  }

  if (cached) {
    responseCache.delete(cacheKey)
  }

  return { cacheKey, cacheTag, cachedResponse: null }
}

export function storeDictionaryResponse(
  accessToken: string,
  cacheTag: string,
  cacheKey: string,
  response: Promise<unknown>,
) {
  responseCache.set(cacheKey, {
    accessToken,
    expiresAt: Date.now() + dictionaryResponseCacheLifetimeMs,
    response,
    tag: cacheTag,
  })

  response.catch(() => {
    if (responseCache.get(cacheKey)?.response === response) {
      responseCache.delete(cacheKey)
    }
  })
}

export function invalidateDictionaryResponseCache(accessToken: string, mutationTag: string | null) {
  if (!mutationTag) {
    return
  }

  const invalidatedTags = cacheDependencies[mutationTag] ?? [mutationTag]
  for (const tag of invalidatedTags) {
    const versionKey = `${accessToken}\n${tag}`
    cacheVersions.set(versionKey, getCacheVersion(accessToken, tag) + 1)
  }

  for (const [cacheKey, entry] of responseCache) {
    if (entry.accessToken === accessToken && invalidatedTags.includes(entry.tag)) {
      responseCache.delete(cacheKey)
    }
  }
}
