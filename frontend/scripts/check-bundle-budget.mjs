import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs'
import { dirname, extname, join, resolve } from 'node:path'
import { gzipSync } from 'node:zlib'

const budget = {
  mainJsGzipBytes: 180 * 1024,
  initialJsGzipBytes: 110 * 1024,
  mainCssGzipBytes: 40 * 1024,
  totalAssetsGzipBytes: 268 * 1024,
}

function formatBytes(bytes) {
  return `${(bytes / 1024).toFixed(1)} KiB`
}

function formatExactBudget(bytes, limit) {
  return `${formatBytes(bytes)} / ${formatBytes(limit)} (${bytes} / ${limit} bytes; remaining ${limit - bytes} bytes)`
}

function collectAssetFiles(directory) {
  return readdirSync(directory)
    .map((fileName) => join(directory, fileName))
    .filter((filePath) => statSync(filePath).isFile())
    .filter((filePath) => ['.js', '.css'].includes(extname(filePath)))
}

function measureGzipBytes(filePath) {
  return gzipSync(readFileSync(filePath)).length
}

function collectStaticJsGraph(entryPath, visited = new Set()) {
  if (visited.has(entryPath)) {
    return visited
  }

  visited.add(entryPath)
  const source = readFileSync(entryPath, 'utf8')
  const importSpecifiers = [
    ...source.matchAll(/\bfrom["'](\.\/[^"']+\.js)["']/g),
    ...source.matchAll(/\bimport["'](\.\/[^"']+\.js)["']/g),
  ].map((match) => match[1])

  for (const specifier of importSpecifiers) {
    const dependencyPath = resolve(dirname(entryPath), specifier)
    if (existsSync(dependencyPath)) {
      collectStaticJsGraph(dependencyPath, visited)
    }
  }

  return visited
}

function fail(message) {
  console.error(message)
  process.exitCode = 1
}

const distIndex = process.argv.indexOf('--dist')
const distPath = resolve(process.cwd(), distIndex === -1 ? 'dist' : process.argv[distIndex + 1] ?? 'dist')
const assetsPath = join(distPath, 'assets')

if (!existsSync(assetsPath)) {
  fail(`Bundle budget check failed: ${assetsPath} does not exist. Run npm run build first.`)
} else {
  const assets = collectAssetFiles(assetsPath)
  const jsAssets = assets.filter((filePath) => extname(filePath) === '.js')
  const cssAssets = assets.filter((filePath) => extname(filePath) === '.css')

  if (jsAssets.length === 0) {
    fail('Bundle budget check failed: no JS assets found.')
  }

  if (cssAssets.length === 0) {
    fail('Bundle budget check failed: no CSS assets found.')
  }

  const measuredAssets = assets.map((filePath) => ({ filePath, gzipBytes: measureGzipBytes(filePath) }))
  const totalAssetsGzipBytes = measuredAssets.reduce((total, asset) => total + asset.gzipBytes, 0)
  const indexHtml = readFileSync(join(distPath, 'index.html'), 'utf8')
  const entrySource = /<script\b[^>]*\bsrc=["']([^"']+\.js)["']/.exec(indexHtml)?.[1]
  if (!entrySource) {
    fail('Bundle budget check failed: production entry script was not found in index.html.')
  }
  const initialJsAssets = entrySource
    ? collectStaticJsGraph(join(distPath, entrySource.replace(/^\/+/, '')))
    : new Set()
  const initialJsGzipBytes = [...initialJsAssets].reduce((total, filePath) => total + measureGzipBytes(filePath), 0)
  const largestJsAsset = measuredAssets
    .filter((asset) => extname(asset.filePath) === '.js')
    .sort((left, right) => right.gzipBytes - left.gzipBytes)[0]
  const largestCssAsset = measuredAssets
    .filter((asset) => extname(asset.filePath) === '.css')
    .sort((left, right) => right.gzipBytes - left.gzipBytes)[0]

  console.log(`main JS gzip: ${formatBytes(largestJsAsset.gzipBytes)} / ${formatBytes(budget.mainJsGzipBytes)}`)
  console.log(`initial JS gzip: ${formatBytes(initialJsGzipBytes)} / ${formatBytes(budget.initialJsGzipBytes)}`)
  console.log(`main CSS gzip: ${formatBytes(largestCssAsset.gzipBytes)} / ${formatBytes(budget.mainCssGzipBytes)}`)
  console.log(`total JS/CSS gzip: ${formatExactBudget(totalAssetsGzipBytes, budget.totalAssetsGzipBytes)}`)

  if (largestJsAsset.gzipBytes > budget.mainJsGzipBytes) {
    fail(`Bundle budget check failed: main JS gzip exceeds ${formatBytes(budget.mainJsGzipBytes)}.`)
  }

  if (initialJsGzipBytes > budget.initialJsGzipBytes) {
    fail(`Bundle budget check failed: initial JS gzip exceeds ${formatBytes(budget.initialJsGzipBytes)}.`)
  }

  if (largestCssAsset.gzipBytes > budget.mainCssGzipBytes) {
    fail(`Bundle budget check failed: main CSS gzip exceeds ${formatBytes(budget.mainCssGzipBytes)}.`)
  }

  if (totalAssetsGzipBytes > budget.totalAssetsGzipBytes) {
    fail(`Bundle budget check failed: total JS/CSS gzip exceeds ${formatBytes(budget.totalAssetsGzipBytes)}.`)
  }
}
