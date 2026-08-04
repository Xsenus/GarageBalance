export const maximumAccessImportFileSizeMegabytes = 50
export const maximumAccessImportFileSizeBytes = maximumAccessImportFileSizeMegabytes * 1024 * 1024

export function validateAccessImportFileSize(file: File): string | null {
  return file.size > maximumAccessImportFileSizeBytes
    ? `Файл Access превышает допустимый размер ${maximumAccessImportFileSizeMegabytes} МБ.`
    : null
}
