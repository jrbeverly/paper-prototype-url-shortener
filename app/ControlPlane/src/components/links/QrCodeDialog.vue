<script setup lang="ts">
import { ref, watch, nextTick } from 'vue'
import QRCode from 'qrcode'

const visible = defineModel<boolean>({ default: false })

const props = defineProps<{
  shortUrl: string
}>()

const canvasRef = ref<HTMLCanvasElement | null>(null)

async function renderQr() {
  await nextTick()
  if (!canvasRef.value || !props.shortUrl) return
  await QRCode.toCanvas(canvasRef.value, props.shortUrl, {
    errorCorrectionLevel: 'M',
    width: 256,
    margin: 2,
    color: {
      dark: '#000000',
      light: '#ffffff',
    },
  })
}

watch(
  visible,
  (val) => {
    if (val) renderQr()
  },
  { immediate: true }
)

watch(
  () => props.shortUrl,
  () => {
    if (visible.value) renderQr()
  }
)

function downloadPng() {
  if (!canvasRef.value) return
  const dataUrl = canvasRef.value.toDataURL('image/png')
  const link = document.createElement('a')
  link.href = dataUrl
  link.download = `qr-${slugFileName()}.png`
  link.click()
}

async function downloadSvg() {
  const svg = await QRCode.toString(props.shortUrl, {
    type: 'svg',
    errorCorrectionLevel: 'M',
    margin: 2,
    color: {
      dark: '#000000',
      light: '#ffffff',
    },
  })
  const blob = new Blob([svg], { type: 'image/svg+xml' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `qr-${slugFileName()}.svg`
  link.click()
  URL.revokeObjectURL(url)
}

function slugFileName(): string {
  const url = props.shortUrl
  const lastSlash = url.lastIndexOf('/')
  return lastSlash >= 0 ? url.slice(lastSlash + 1) : 'link'
}
</script>

<template>
  <v-dialog v-model="visible" max-width="380" data-testid="qr-code-dialog">
    <v-card>
      <v-card-title class="d-flex align-center">
        QR Code
        <v-spacer />
        <v-btn
          icon="mdi-close"
          variant="text"
          size="small"
          data-testid="qr-close-btn"
          @click="visible = false"
        />
      </v-card-title>

      <v-card-text class="text-center pa-6">
        <canvas
          ref="canvasRef"
          width="256"
          height="256"
          class="qr-canvas"
          style="max-width: 100%; border-radius: 8px"
          data-testid="qr-canvas"
        />
        <p class="text-caption text-medium-emphasis mt-3 mb-0" data-testid="qr-url-text">
          {{ shortUrl }}
        </p>
      </v-card-text>

      <v-divider />

      <v-card-actions class="pa-4">
        <v-btn
          variant="outlined"
          prepend-icon="mdi-download"
          data-testid="qr-download-png-btn"
          @click="downloadPng"
        >
          PNG
        </v-btn>
        <v-btn
          variant="outlined"
          prepend-icon="mdi-download"
          class="ml-2"
          data-testid="qr-download-svg-btn"
          @click="downloadSvg"
        >
          SVG
        </v-btn>
        <v-spacer />
        <v-btn variant="text" data-testid="qr-close-bottom-btn" @click="visible = false">
          Close
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
