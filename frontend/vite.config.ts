import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export function getManualChunkName(id: string): string | undefined {
  if (id.includes('vite/preload-helper')) return 'app-runtime'
  if (/[/\\]src[/\\]services[/\\](apiFetch|authenticatedApiFetch|dictionaryResponseCache)\./.test(id)) return 'app-runtime'
  if (/[/\\]src[/\\]shared[/\\]retryableLazyLoader\./.test(id)) return 'app-runtime'
  // Keep the authenticated workspace outside the login graph, but split its
  // largest business areas so every production chunk stays comfortably below
  // Vite's warning threshold.
  if (/[/\\]src[/\\]features[/\\](finance|funds|import)[/\\]/.test(id)) return 'workspace-finance'
  if (/[/\\]src[/\\]features[/\\](meterReadings|contractors|tariffs)[/\\]/.test(id)) return 'workspace-operations'
  if (/[/\\]src[/\\]features[/\\](settings[/\\]PasswordPanel|users[/\\]UserManagementPanel)\./.test(id)) return 'app-runtime'
  if (id.includes('lucide-react')) return 'app-runtime'
  if (/[/\\]node_modules[/\\](react|react-dom|scheduler)[/\\]/.test(id)) return 'app-runtime'
  if (/[/\\]src[/\\]features[/\\](reports[/\\]ReportPanel|audit[/\\]AuditPanel|releases[/\\]ReleasePanel)\./.test(id)) return 'workspace-operations'
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
