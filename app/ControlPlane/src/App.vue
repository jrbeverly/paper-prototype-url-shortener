<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { VueQueryDevtools } from '@tanstack/vue-query-devtools'
import { useUIStore } from '@/stores/ui'
import AppLayout from '@/components/AppLayout.vue'

const isDev = import.meta.env.DEV
const route = useRoute()
const uiStore = useUIStore()

const showLayout = computed(() => route.meta.requiresAuth === true)
</script>

<template>
  <v-app :theme="uiStore.theme">
    <AppLayout v-if="showLayout" />
    <v-main v-else>
      <RouterView />
    </v-main>
    <VueQueryDevtools v-if="isDev" />
  </v-app>
</template>
