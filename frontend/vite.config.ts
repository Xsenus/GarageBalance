import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export function getManualChunkName(id: string): string | undefined {
  if (id.includes('vite/preload-helper')) return 'app-runtime'
  if (/[/\\]src[/\\]services[/\\](apiFetch|authenticatedApiFetch|dictionaryResponseCache)\./.test(id)) return 'app-runtime'
  if (/[/\\]src[/\\]shared[/\\]retryableLazyLoader\./.test(id)) return 'app-runtime'
  // Login uses these shared primitives. Keep their ownership explicit so moving
  // authenticated panels cannot pull the financial workspace into the entry graph.
  if (/[/\\]src[/\\]shared[/\\](formFeedback|validation|AsyncState|accessControl)\./.test(id)) return 'app-runtime'
  // Keep the authenticated workspace outside the login graph, but split its
  // largest business areas and keep their size visible in the bundle budget.
  if (/[/\\]src[/\\]features[/\\](finance|funds|import|tariffs)[/\\]/.test(id)) return 'workspace-finance'
  if (/[/\\]src[/\\]features[/\\](meterReadings|contractors|dictionaries)[/\\]/.test(id)) return 'workspace-operations'
  // Dictionary form metadata is only needed after authentication, not at login.
  if (/[/\\]src[/\\]shared[/\\]dictionaryWorkbench\./.test(id)) return 'workspace-operations'
  if (/[/\\]src[/\\]features[/\\](settings[/\\]PasswordPanel|users[/\\]UserManagementPanel)\./.test(id)) return 'workspace-operations'
  if (id.includes('lucide-react')) return 'app-runtime'
  if (/[/\\]node_modules[/\\](react|react-dom|scheduler)[/\\]/.test(id)) return 'app-runtime'
  if (/[/\\]src[/\\]features[/\\](reports[/\\]ReportPanel|audit[/\\]AuditPanel|releases[/\\]ReleasePanel)\./.test(id)) return 'workspace-operations'
  if (/[/\\]src[/\\]shared[/\\](DecimalTextInput|EditableCombobox|editableComboboxMatching|FormField|LocalizedDatePicker|MoneyInput|SelectControl|TablePagination|changePreview|fileExports|MeterReadingInput|PhoneInput|PhoneListInput|prototypeEditing|reportFilters|ReportPeriodQuickSelect)\./.test(id)) return 'app-runtime'
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
