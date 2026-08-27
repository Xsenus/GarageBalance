import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export function getManualChunkName(id: string): string | undefined {
  if (/[/\\]src[/\\]features[/\\](finance|funds)[/\\]/.test(id)) return 'financial-operations'
  if (/[/\\]src[/\\]features[/\\](contractors|tariffs)[/\\]/.test(id)) return 'cooperative-setup'
  if (/[/\\]src[/\\]features[/\\](settings[/\\]PasswordPanel|users[/\\]UserManagementPanel)\./.test(id)) return 'app-runtime'
  if (id.includes('lucide-react')) return 'app-runtime'
  if (/[/\\]src[/\\]features[/\\](reports[/\\]ReportPanel|audit[/\\]AuditPanel|releases[/\\]ReleasePanel)\./.test(id)) return 'reporting'
  if (/[/\\]src[/\\]shared[/\\](EditableCombobox|editableComboboxMatching|FormField|LocalizedDatePicker|MoneyInput|SelectControl|TablePagination|changePreview|dictionaryWorkbench|fileExports|MeterReadingInput|PhoneInput|prototypeEditing|reportFilters|ReportPeriodQuickSelect)\./.test(id)) return 'app-runtime'
  return undefined
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    cssMinify: 'lightningcss',
    modulePreload: { polyfill: false },
    rollupOptions: {
      output: {
        manualChunks: getManualChunkName,
      },
    },
  },
})
