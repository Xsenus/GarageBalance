export type ServiceRecordView = 'active' | 'deleted'

export function selectServiceViewRecords<T>(
  records: readonly T[],
  view: ServiceRecordView,
  isArchived: (record: T) => boolean,
): T[] {
  return records.filter((record) => isArchived(record) === (view === 'deleted'))
}
