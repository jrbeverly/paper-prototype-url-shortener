<template>
  <v-container class="fill-height" fluid>
    <v-row align="center" justify="center">
      <v-col cols="12" sm="10" md="6" class="text-center">
        <v-icon icon="mdi-link-off" size="72" color="medium-emphasis" class="mb-4" />

        <p class="text-h1 font-weight-bold text-medium-emphasis mb-2">404</p>
        <h1 class="text-h4 font-weight-medium mb-3">Page not found</h1>
        <p class="text-body-1 text-medium-emphasis mb-8">
          The page you're looking for doesn't exist or has been moved.
        </p>

        <v-text-field
          v-model="searchQuery"
          label="Search for a link"
          prepend-inner-icon="mdi-magnify"
          variant="outlined"
          class="mb-6"
          clearable
          hide-details
          @keyup.enter="search"
        />

        <div class="d-flex gap-3 justify-center flex-wrap">
          <v-btn color="primary" size="large" :to="{ name: 'dashboard' }">
            <v-icon start>mdi-view-dashboard</v-icon>
            Go to Dashboard
          </v-btn>
          <v-btn variant="outlined" size="large" :to="{ path: '/' }">
            <v-icon start>mdi-home</v-icon>
            Go to Home
          </v-btn>
        </div>
      </v-col>
    </v-row>
  </v-container>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'

const router = useRouter()
const searchQuery = ref('')

function search() {
  const q = searchQuery.value.trim()
  if (!q) return
  router.push({ name: 'links', query: { q } })
}
</script>
