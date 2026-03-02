<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import MindMapCanvas from '../components/MindMapCanvas.vue'
import { getSession } from '../services/api'
import { pbCreateShare, pbGetMap, pbUpdateMap } from '../services/pb'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()
const editorRef = ref(null)
const busy = ref(false)
const saving = ref(false)
const messageKey = ref('')
const error = ref('')
const shareLink = ref('')
const session = ref(getSession())
const isLoaded = ref(false)
const suppressWatch = ref(false)
const lastSavedSignature = ref('')
const saveQueued = ref(false)
const map = reactive({
  id: '',
  title: '',
  contentJson: '{"nodes":[],"edges":[],"meta":{"backgroundColor":"#ffffff"}}',
  shareCode: '',
})
let saveTimer = null

const AUTO_SAVE_DELAY_MS = 700
const message = computed(() => (messageKey.value ? t(messageKey.value) : ''))
const autoSaveStatus = computed(() => (saving.value ? t('editor.saving') : t('editor.enabled')))

onMounted(async () => {
  if (!session.value?.token) {
    error.value = t('editor.loginFirst')
    return
  }
  await loadMap()
  if (!error.value) {
    await openSharePageByDefault()
  }
})

onBeforeUnmount(() => {
  if (saveTimer) {
    clearTimeout(saveTimer)
    saveTimer = null
  }
})

watch(
  [() => map.title, () => map.contentJson],
  () => {
    if (!isLoaded.value || suppressWatch.value) return
    scheduleAutoSave()
  },
  { deep: false }
)

function buildSignature() {
  return JSON.stringify({
    title: map.title?.trim() || '',
    contentJson: map.contentJson || '',
  })
}

function scheduleAutoSave() {
  if (saveTimer) {
    clearTimeout(saveTimer)
  }
  saveTimer = setTimeout(() => {
    saveTimer = null
    void saveMap(false)
  }, AUTO_SAVE_DELAY_MS)
}

async function loadMap() {
  busy.value = true
  error.value = ''
  messageKey.value = ''
  isLoaded.value = false
  try {
    const payload = await pbGetMap(route.params.id)
    suppressWatch.value = true
    map.id = payload.id
    map.title = payload.title
    map.contentJson = payload.contentJson || '{"nodes":[],"edges":[],"meta":{"backgroundColor":"#ffffff"}}'
    map.shareCode = payload.shareCode || ''
    shareLink.value = payload.shareCode ? `${window.location.origin}/share/${payload.shareCode}` : ''
    lastSavedSignature.value = buildSignature()
    isLoaded.value = true
  } catch (err) {
    error.value = err.message
  } finally {
    suppressWatch.value = false
    busy.value = false
  }
}

async function openSharePageByDefault() {
  if (!map.id) return
  busy.value = true
  error.value = ''
  try {
    if (!map.shareCode) {
      const result = await pbCreateShare(map.id)
      map.shareCode = result.shareCode || ''
      shareLink.value = `${window.location.origin}${result.relativeUrl}`
    }
    if (map.shareCode) {
      await router.replace(`/share/${map.shareCode}`)
    }
  } catch (err) {
    error.value = err.message
  } finally {
    busy.value = false
  }
}

async function saveMap(force = true) {
  if (!map.id || !isLoaded.value) return
  if (saveTimer && force) {
    clearTimeout(saveTimer)
    saveTimer = null
  }

  const currentSignature = buildSignature()
  if (!force && currentSignature === lastSavedSignature.value) {
    return
  }

  if (saving.value) {
    saveQueued.value = true
    return
  }

  saving.value = true
  if (force) {
    messageKey.value = ''
  }
  error.value = ''
  try {
    const payload = await pbUpdateMap(map.id, map.title, map.contentJson)
    suppressWatch.value = true
    map.title = payload.title
    map.contentJson = payload.contentJson
    lastSavedSignature.value = JSON.stringify({
      title: payload.title?.trim() || '',
      contentJson: payload.contentJson || '',
    })
    messageKey.value = force ? 'editor.saved' : 'editor.autoSaved'
  } catch (err) {
    error.value = err.message
  } finally {
    suppressWatch.value = false
    saving.value = false
    if (saveQueued.value) {
      saveQueued.value = false
      void saveMap(false)
    }
  }
}

async function saveNow() {
  if (editorRef.value) {
    map.contentJson = editorRef.value.getGraphJson()
  }
  await saveMap(true)
}

async function createShareLink() {
  if (!map.id) return
  if (editorRef.value) {
    map.contentJson = editorRef.value.getGraphJson()
  }
  await saveMap(true)
  busy.value = true
  try {
    const result = await pbCreateShare(map.id)
    map.shareCode = result.shareCode || map.shareCode
    shareLink.value = `${window.location.origin}${result.relativeUrl}`
    messageKey.value = 'editor.shareGenerated'
  } catch (err) {
    error.value = err.message
  } finally {
    busy.value = false
  }
}

async function backHome() {
  await router.push('/')
}
</script>

<template>
  <main class="page">
    <section class="panel editor-page">
      <div class="toolbar">
        <h1>{{ t('editor.title') }}</h1>
        <div class="actions">
          <button @click="backHome">{{ t('editor.backHome') }}</button>
        </div>
      </div>
      <p class="sub">{{ t('editor.keyboardHint') }}</p>

      <div v-if="!session?.token" class="err">{{ t('editor.loginFirst') }}</div>
      <template v-else>
        <label>
          {{ t('editor.titleLabel') }}
          <input v-model.trim="map.title" />
        </label>

        <MindMapCanvas ref="editorRef" v-model="map.contentJson" :height="560" />

        <div class="actions">
          <button class="primary" :disabled="busy || saving" @click="saveNow">{{ t('editor.saveNow') }}</button>
          <button :disabled="busy" @click="createShareLink">{{ t('editor.generateShareLink') }}</button>
          <a v-if="shareLink" :href="shareLink" target="_blank">{{ t('editor.openSharePage') }}</a>
        </div>
        <p class="hint">{{ t('editor.autoSave') }}: {{ autoSaveStatus }}</p>

        <div v-if="shareLink" class="share-box">
          <div>{{ t('editor.shareUrl') }}:</div>
          <a :href="shareLink" target="_blank">{{ shareLink }}</a>
        </div>
      </template>

      <p v-if="message" class="ok">{{ message }}</p>
      <p v-if="error" class="err">{{ error }}</p>
    </section>
  </main>
</template>
