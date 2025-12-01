import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { nextTick } from 'vue'
import QrCodeDialog from '../QrCodeDialog.vue'

vi.mock('qrcode', () => ({
  default: {
    toCanvas: vi.fn(),
    toString: vi.fn(),
  },
}))

const vuetify = createVuetify({ components, directives })

vi.stubGlobal(
  'ResizeObserver',
  class {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
)

vi.stubGlobal('visualViewport', {
  height: 768,
  width: 1024,
  offsetTop: 0,
  addEventListener: vi.fn(),
  removeEventListener: vi.fn(),
})

HTMLCanvasElement.prototype.toDataURL = vi.fn(() => 'data:image/png;base64,mockdata')

function mountComponent(visible = true, shortUrl = 'https://go.acme.com/launch') {
  return mount(QrCodeDialog, {
    props: { modelValue: visible, shortUrl, 'onUpdate:modelValue': vi.fn() },
    global: {
      plugins: [vuetify],
      stubs: {
        'v-dialog': {
          template: '<div><slot /></div>',
          props: ['modelValue'],
        },
      },
    },
  })
}

describe('QrCodeDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders the dialog when visible', () => {
    const wrapper = mountComponent(true)
    expect(wrapper.find('[data-testid="qr-code-dialog"]').exists()).toBe(true)
  })

  it('displays the short URL text', () => {
    const wrapper = mountComponent(true, 'https://go.acme.com/launch')
    expect(wrapper.find('[data-testid="qr-url-text"]').text()).toBe('https://go.acme.com/launch')
  })

  it('renders a canvas for the QR code', () => {
    const wrapper = mountComponent(true)
    expect(wrapper.find('[data-testid="qr-canvas"]').exists()).toBe(true)
  })

  it('renders PNG and SVG download buttons', () => {
    const wrapper = mountComponent(true)
    expect(wrapper.find('[data-testid="qr-download-png-btn"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="qr-download-svg-btn"]').exists()).toBe(true)
  })

  it('calls QRCode.toCanvas when dialog opens', async () => {
    const qrcode = await import('qrcode')
    mountComponent(true, 'https://go.acme.com/launch')
    // The watch(visible, ...) with { immediate: true } fires during mount
    // renderQr awaits nextTick internally
    await nextTick()
    await nextTick()
    expect(qrcode.default.toCanvas).toHaveBeenCalledWith(
      expect.any(HTMLCanvasElement),
      'https://go.acme.com/launch',
      expect.any(Object)
    )
  })

  it('triggers PNG download without error', async () => {
    const wrapper = mountComponent(true)
    await nextTick()
    await wrapper.find('[data-testid="qr-download-png-btn"]').trigger('click')
    // No assertion needed — the test passes if no error is thrown
  })

  it('re-renders QR code when shortUrl changes while dialog is open', async () => {
    const qrcode = await import('qrcode')
    const wrapper = mountComponent(true, 'https://go.acme.com/launch')
    await nextTick()
    await nextTick()
    vi.clearAllMocks()

    await wrapper.setProps({ shortUrl: 'https://go.acme.com/new-slug' })
    await nextTick()
    await nextTick()

    expect(qrcode.default.toCanvas).toHaveBeenCalledWith(
      expect.any(HTMLCanvasElement),
      'https://go.acme.com/new-slug',
      expect.any(Object)
    )
  })

  it('triggers SVG download without error', async () => {
    const qrcode = await import('qrcode')
    vi.mocked(qrcode.default.toString).mockResolvedValue('<svg>mock</svg>')
    URL.createObjectURL = vi.fn(() => 'blob:mock-url')
    URL.revokeObjectURL = vi.fn()

    const wrapper = mountComponent(true)
    await nextTick()
    await wrapper.find('[data-testid="qr-download-svg-btn"]').trigger('click')
    await nextTick()

    expect(qrcode.default.toString).toHaveBeenCalledWith(
      'https://go.acme.com/launch',
      expect.objectContaining({ type: 'svg' })
    )
  })
})
