import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (id.includes('lucide-react')) return 'icons'
          if (/[/\\]src[/\\]shared[/\\](FormField|LocalizedDatePicker|MoneyInput|SelectControl|TablePagination)\./.test(id)) return 'form-controls'
          if (/[/\\]src[/\\]shared[/\\](changePreview|dictionaryWorkbench|fileExports|MeterReadingInput|PhoneInput|prototypeEditing|reportFilters|ReportPeriodQuickSelect)\./.test(id)) return 'workflow-tools'
          return undefined
        },
      },
    },
  },
})
