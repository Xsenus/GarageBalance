import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export function getManualChunkName(id: string): string | undefined {
  if (id.includes('vite/preload-helper')) return 'app-runtime'
  if (/[/\\]src[/\\]services[/\\](apiFetch|authenticatedApiFetch|dictionaryResponseCache)\./.test(id)) return 'app-runtime'
  if (/[/\\]src[/\\]shared[/\\]retryableLazyLoader\./.test(id)) return 'app-runtime'
  // AppShell already imports these accounting sections together. Keep their shared
  // code compressed in one workspace chunk while preserving the separate login runtime.
  if (/[/\\]src[/\\]features[/\\](finance|meterReadings|contractors|tariffs)[/\\]/.test(id)) return 'workspace-accounting'
  if (/[/\\]src[/\\]features[/\\]funds[/\\]/.test(id)) return 'workspace-accounting'
  if (/[/\\]src[/\\]features[/\\](settings[/\\]PasswordPanel|users[/\\]UserManagementPanel)\./.test(id)) return 'app-runtime'
  if (id.includes('lucide-react')) return 'app-runtime'
  if (/[/\\]node_modules[/\\](react|react-dom|scheduler)[/\\]/.test(id)) return 'app-runtime'
  if (/[/\\]src[/\\]features[/\\](reports[/\\]ReportPanel|audit[/\\]AuditPanel|releases[/\\]ReleasePanel)\./.test(id)) return 'workspace-accounting'
  if (/[/\\]src[/\\]shared[/\\](EditableCombobox|editableComboboxMatching|FormField|LocalizedDatePicker|MoneyInput|SelectControl|TablePagination|changePreview|dictionaryWorkbench|fileExports|MeterReadingInput|PhoneInput|prototypeEditing|reportFilters|ReportPeriodQuickSelect)\./.test(id)) return 'app-runtime'
  return undefined
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    cssMinify: 'lightningcss',
    modulePreload: false,
    rollupOptions: {
      output: {
        manualChunks: getManualChunkName,
      },
    },
  },
})
