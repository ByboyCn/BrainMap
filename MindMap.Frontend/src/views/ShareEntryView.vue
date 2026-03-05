<script setup>
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import ShareView from './ShareView.vue'
import TodoShareView from './TodoShareView.vue'
import { pbGetShared } from '../services/pb'

const route = useRoute()
const { t } = useI18n()
const loading = ref(true)
const error = ref('')
const docType = ref('mindmap')
const payload = ref(null)

const currentView = computed(() => (docType.value === 'todo' ? TodoShareView : ShareView))

function parseDocType(contentJson) {
  try {
    const parsed = JSON.parse(contentJson || '{}')
    if (String(parsed?.docType || '').toLowerCase() === 'todo') {
      return 'todo'
    }
  } catch {
    // ignore
  }
  return 'mindmap'
}

onMounted(async () => {
  loading.value = true
  error.value = ''
  try {
    const result = await pbGetShared(String(route.params.shareCode || ''))
    payload.value = result
    docType.value = parseDocType(result.contentJson)
  } catch (err) {
    error.value = err.message
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <main v-if="loading" class="share-full-page">
    <div class="share-entry-state">{{ t('share.loading') }}</div>
  </main>
  <main v-else-if="error" class="share-full-page">
    <div class="share-entry-state err">{{ error }}</div>
  </main>
  <component :is="currentView" v-else :initial-payload="payload" />
</template>
