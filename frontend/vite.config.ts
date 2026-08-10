import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    cssMinify: 'lightningcss',
    modulePreload: { polyfill: false },
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (id.includes('lucide-react')) return 'shared-ui'
          if (/[/\\]src[/\\]features[/\\](reports[/\\]ReportPanel|audit[/\\]AuditPanel)\./.test(id)) return 'reporting'
          if (/[/\\]src[/\\]shared[/\\](FormField|LocalizedDatePicker|MoneyInput|SelectControl|TablePagination|changePreview|dictionaryWorkbench|fileExports|MeterReadingInput|PhoneInput|prototypeEditing|reportFilters|ReportPeriodQuickSelect)\./.test(id)) return 'shared-ui'
          return undefined
        },
      },
    },
  },
})
