import { describe, expect, it } from 'vitest'
import { maximumAccessImportFileSizeBytes, validateAccessImportFileSize } from './importFileLimits'

function fileWithSize(size: number) {
  const file = new File(['access'], 'archive.accdb')
  Object.defineProperty(file, 'size', { value: size })
  return file
}

describe('validateAccessImportFileSize', () => {
  it('accepts a file at the shared 50 MB boundary', () => {
    expect(validateAccessImportFileSize(fileWithSize(maximumAccessImportFileSizeBytes))).toBeNull()
  })

  it('returns a clear error before an oversized file is uploaded', () => {
    expect(validateAccessImportFileSize(fileWithSize(maximumAccessImportFileSizeBytes + 1)))
      .toBe('Файл Access превышает допустимый размер 50 МБ.')
  })
})
