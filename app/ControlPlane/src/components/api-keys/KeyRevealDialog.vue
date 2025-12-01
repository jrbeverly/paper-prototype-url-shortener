<script setup lang="ts">
import { ref } from 'vue'
import type { ApiKeyCreated } from '@/types/apiKeys'

const visible = defineModel<boolean>({ default: false })

const props = defineProps<{
  createdKey: ApiKeyCreated
}>()

const copied = ref(false)

async function copyKey() {
  await navigator.clipboard.writeText(props.createdKey.key)
  copied.value = true
  setTimeout(() => {
    copied.value = false
  }, 2000)
}

function handleClose() {
  visible.value = false
}
</script>

<template>
  <v-dialog v-model="visible" max-width="560" persistent>
    <v-card>
      <v-card-item>
        <template #prepend>
          <v-icon color="success">mdi-key-variant</v-icon>
        </template>
        <v-card-title>API key created</v-card-title>
      </v-card-item>

      <v-card-text>
        <v-alert type="warning" variant="tonal" class="mb-4" data-testid="reveal-warning">
          <strong>Save this key now.</strong> For security reasons, the full key is only shown once
          and cannot be retrieved after you close this dialog.
        </v-alert>

        <p class="text-body-2 text-medium-emphasis mb-2">
          Key name: <strong class="text-high-emphasis">{{ createdKey.name }}</strong>
        </p>

        <v-sheet
          rounded="lg"
          color="surface-variant"
          class="pa-3 mb-3 d-flex align-center ga-2"
          data-testid="key-value-container"
        >
          <code
            class="text-body-2 flex-1"
            style="word-break: break-all; font-family: monospace"
            data-testid="key-value"
          >
            {{ createdKey.key }}
          </code>
          <v-btn
            :icon="copied ? 'mdi-check' : 'mdi-content-copy'"
            :color="copied ? 'success' : undefined"
            variant="text"
            size="small"
            data-testid="copy-key-btn"
            @click="copyKey"
          >
            <v-tooltip activator="parent" location="top">
              {{ copied ? 'Copied!' : 'Copy key' }}
            </v-tooltip>
          </v-btn>
        </v-sheet>

        <p class="text-caption text-medium-emphasis">
          Store this key in a secure location such as a secrets manager. Do not share it or commit
          it to source control.
        </p>
      </v-card-text>

      <v-divider />

      <v-card-actions class="pa-4">
        <v-spacer />
        <v-btn color="primary" variant="flat" data-testid="reveal-done-btn" @click="handleClose">
          I've saved my key
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
