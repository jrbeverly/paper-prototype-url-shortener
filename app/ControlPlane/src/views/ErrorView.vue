<template>
  <v-container class="fill-height" fluid>
    <v-row align="center" justify="center">
      <v-col cols="12" sm="10" md="6" class="text-center">
        <v-icon :icon="errorIcon" size="72" color="medium-emphasis" class="mb-4" />

        <p class="text-h1 font-weight-bold text-medium-emphasis mb-2">{{ code }}</p>
        <h1 class="text-h4 font-weight-medium mb-3">{{ errorTitle }}</h1>
        <p class="text-body-1 text-medium-emphasis mb-8">{{ errorDescription }}</p>

        <div class="d-flex gap-3 justify-center flex-wrap">
          <v-btn color="primary" size="large" :to="{ name: 'dashboard' }">
            <v-icon start>mdi-view-dashboard</v-icon>
            Go to Dashboard
          </v-btn>
          <v-btn variant="outlined" size="large" @click="goBack">
            <v-icon start>mdi-arrow-left</v-icon>
            Go Back
          </v-btn>
        </div>
      </v-col>
    </v-row>
  </v-container>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'

const props = withDefaults(
  defineProps<{
    code: number
    message?: string
  }>(),
  {
    code: 500,
    message: undefined,
  }
)

const router = useRouter()

const errorIcon = computed(() => {
  switch (props.code) {
    case 401:
      return 'mdi-lock'
    case 403:
      return 'mdi-lock-outline'
    case 408:
      return 'mdi-clock-alert-outline'
    case 502:
    case 503:
      return 'mdi-cloud-off-outline'
    case 500:
      return 'mdi-server-off'
    default:
      return 'mdi-alert-circle-outline'
  }
})

const errorTitle = computed(() => {
  if (props.message) return props.message
  switch (props.code) {
    case 400:
      return 'Bad Request'
    case 401:
      return 'Unauthorized'
    case 403:
      return 'Access Forbidden'
    case 408:
      return 'Request Timeout'
    case 500:
      return 'Internal Server Error'
    case 502:
      return 'Bad Gateway'
    case 503:
      return 'Service Unavailable'
    default:
      return 'An Error Occurred'
  }
})

const errorDescription = computed(() => {
  switch (props.code) {
    case 400:
      return 'The request could not be understood. Please check your input and try again.'
    case 401:
      return 'You need to sign in to access this resource.'
    case 403:
      return "You don't have permission to access this page."
    case 408:
      return 'The request took too long to complete. Please try again.'
    case 500:
      return "Something went wrong on our end. We're working to fix it."
    case 502:
      return 'The server received an invalid response. Please try again later.'
    case 503:
      return 'The service is temporarily unavailable. Please try again later.'
    default:
      return 'Something unexpected happened. Please try again.'
  }
})

function goBack() {
  router.back()
}
</script>
