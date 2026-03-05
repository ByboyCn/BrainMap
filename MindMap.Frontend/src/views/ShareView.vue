<script setup>
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import MindMapCanvas from '../components/MindMapCanvas.vue'
import { setLocale } from '../i18n'
import { getApiBaseUrl, getSession } from '../services/api'
import { pbAddShareHistory, pbCreateShare, pbGetShared, pbUpdateShared } from '../services/pb'

const route = useRoute()
const router = useRouter()
const { t, locale } = useI18n()
const shareCode = route.params.shareCode
const mapTitle = ref('')
const contentJson = ref('{"nodes":[],"edges":[]}')
const displayName = ref(getSession()?.userName || '')
const onlineUsers = ref([])
const connectState = ref('disconnected')
const messageKey = ref('')
const error = ref('')
const boardRef = ref(null)
const mindmapRef = ref(null)
const isLoaded = ref(false)
const suppressWatch = ref(false)
const saving = ref(false)
const lastSavedContent = ref('')
const saveQueued = ref(false)
const remoteApplying = ref(false)
const viewportHeight = ref(window.innerHeight)
const menuOpen = ref(false)
const menuRef = ref(null)
const settingsOpen = ref(false)
const settingsRef = ref(null)
const onlineOpen = ref(false)
const onlineRef = ref(null)
const hadConnectedOnce = ref(false)
const showWsMask = ref(false)
const defaultDocumentTitle = document.title
const session = ref(getSession())
const mapId = ref('')
const shareEnabled = ref(true)
const shareRequireLogin = ref(false)
const shareAllowGuestEdit = ref(true)
const accessSaving = ref(false)

let connection = null
let lastSentAt = 0
let saveTimer = null
let broadcastTimer = null

const AUTO_SAVE_DELAY_MS = 700
const BROADCAST_DELAY_MS = 120
const message = computed(() => (messageKey.value ? t(messageKey.value) : ''))
const myConnectionId = computed(() => connection?.connectionId || '')
const usersForList = computed(() => onlineUsers.value.filter((user) => !!user.connectionId))
const usersForCursor = computed(() =>
  onlineUsers.value.filter((user) => user.connectionId && user.connectionId !== myConnectionId.value)
)
const canvasHeight = computed(() => Math.max(420, viewportHeight.value))
const currentLocale = computed(() => locale.value)
const currentLocaleLabel = computed(() => (currentLocale.value === 'zh' ? t('app.zh') : t('app.en')))
const wsMaskText = computed(() => (currentLocale.value === 'en' ? 'WebSocket reconnecting...' : 'ws正在重新连接中'))
const shareDisplayTitle = computed(() => {
  const name = String(mapTitle.value || '').trim() || String(shareCode || '').trim()
  return `${t('share.mapPrefix')} + ${name}`
})
const canManageShare = computed(() => !!session.value?.token && !!mapId.value)
const isAuthenticated = computed(() => !!session.value?.token)
const shareModeLabel = computed(() => (canEditShared.value ? t('share.modeEditable') : t('share.modeReadonly')))
const canEditShared = computed(() => {
  if (!shareEnabled.value) return false
  if (shareRequireLogin.value) return isAuthenticated.value
  if (!isAuthenticated.value && !shareAllowGuestEdit.value) return false
  return true
})
onMounted(async () => {
  document.addEventListener('click', handleDocumentClick)
  window.addEventListener('resize', handleResize)
  handleResize()
  await loadMap()
  if (!error.value) {
    await connectHub()
  }
})

onBeforeUnmount(async () => {
  document.removeEventListener('click', handleDocumentClick)
  window.removeEventListener('resize', handleResize)
  if (saveTimer) {
    clearTimeout(saveTimer)
    saveTimer = null
  }
  if (broadcastTimer) {
    clearTimeout(broadcastTimer)
    broadcastTimer = null
  }
  document.title = defaultDocumentTitle
  await disconnectHub()
})

watch(contentJson, () => {
  if (!isLoaded.value || suppressWatch.value) return
  if (!canEditShared.value) return
  scheduleAutoSave()
  scheduleBroadcast()
})

watch(
  shareDisplayTitle,
  (nextTitle) => {
    document.title = nextTitle || defaultDocumentTitle
  },
  { immediate: true }
)

function handleResize() {
  viewportHeight.value = window.innerHeight || 800
}

function switchLocale(nextLocale) {
  setLocale(nextLocale)
  menuOpen.value = false
}

function toggleMenu() {
  menuOpen.value = !menuOpen.value
}

function toggleSettings() {
  settingsOpen.value = !settingsOpen.value
}

function toggleOnline() {
  onlineOpen.value = !onlineOpen.value
}

function handleDocumentClick(event) {
  if (menuRef.value && !menuRef.value.contains(event.target)) {
    menuOpen.value = false
  }
  if (settingsRef.value && !settingsRef.value.contains(event.target)) {
    settingsOpen.value = false
  }
  if (onlineRef.value && !onlineRef.value.contains(event.target)) {
    onlineOpen.value = false
  }
}

function scheduleAutoSave() {
  if (saveTimer) {
    clearTimeout(saveTimer)
  }
  saveTimer = setTimeout(() => {
    saveTimer = null
    void saveShared(false)
  }, AUTO_SAVE_DELAY_MS)
}

function scheduleBroadcast() {
  if (broadcastTimer) {
    clearTimeout(broadcastTimer)
  }
  broadcastTimer = setTimeout(() => {
    broadcastTimer = null
    void broadcastSharedContent()
  }, BROADCAST_DELAY_MS)
}

async function loadMap() {
  try {
    const payload = await pbGetShared(shareCode)
    mapId.value = payload.id || ''
    mapTitle.value = payload.title
    shareEnabled.value = !!payload.shareEnabled
    shareRequireLogin.value = !!payload.shareRequireLogin
    shareAllowGuestEdit.value = !!payload.shareAllowGuestEdit
    suppressWatch.value = true
    contentJson.value = payload.contentJson || '{"nodes":[],"edges":[]}'
    lastSavedContent.value = contentJson.value
    isLoaded.value = true
  } catch (err) {
    error.value = err.message
  } finally {
    suppressWatch.value = false
  }
}

async function applyShareAccessMode() {
  if (!canManageShare.value || accessSaving.value) return
  accessSaving.value = true
  error.value = ''
  try {
    const result = await pbCreateShare(
      mapId.value,
      !!shareRequireLogin.value,
      !!shareEnabled.value,
      shareRequireLogin.value ? false : !!shareAllowGuestEdit.value
    )
    shareEnabled.value = !!result.enabled
    shareRequireLogin.value = !!result.requireLogin
    shareAllowGuestEdit.value = !!result.guestCanEdit
    messageKey.value = 'share.accessSaved'
  } catch (err) {
    error.value = err.message
  } finally {
    accessSaving.value = false
  }
}

async function connectHub() {
  if (connection) return

  const token = getSession()?.token || ''
  connection = new HubConnectionBuilder()
    .withUrl(`${getApiBaseUrl()}/hubs/share`, {
      accessTokenFactory: () => token,
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()

  connection.on('OnlineUsers', (users) => {
    onlineUsers.value = users
  })

  connection.on('UserJoined', (user) => {
    const exists = onlineUsers.value.find((item) => item.connectionId === user.connectionId)
    if (!exists) {
      onlineUsers.value = [...onlineUsers.value, user]
    }
  })

  connection.on('UserLeft', (connectionId) => {
    onlineUsers.value = onlineUsers.value.filter((item) => item.connectionId !== connectionId)
  })

  connection.on('CursorMoved', (user) => {
    const list = [...onlineUsers.value]
    const idx = list.findIndex((item) => item.connectionId === user.connectionId)
    if (idx >= 0) {
      list[idx] = user
    } else {
      list.push(user)
    }
    onlineUsers.value = list
  })

  connection.on('ContentUpdated', (payload) => {
    const nextContent = payload?.contentJson
    if (!nextContent || nextContent === contentJson.value) return
    remoteApplying.value = true
    suppressWatch.value = true
    contentJson.value = nextContent
    lastSavedContent.value = nextContent
    setTimeout(() => {
      suppressWatch.value = false
      remoteApplying.value = false
    }, 0)
  })

  connection.onreconnected(async () => {
    try {
      await joinRoom()
      showWsMask.value = false
    } catch (err) {
      connectState.value = 'failed'
      error.value = err?.message || String(err || '')
      if (hadConnectedOnce.value) {
        showWsMask.value = true
      }
    }
  })

  connection.onreconnecting(() => {
    if (hadConnectedOnce.value) {
      showWsMask.value = true
    }
    connectState.value = 'disconnected'
  })

  connection.onclose(() => {
    if (hadConnectedOnce.value) {
      showWsMask.value = true
      connectState.value = 'failed'
    }
  })

  try {
    await connection.start()
    await joinRoom()
  } catch (err) {
    error.value = err.message
    connectState.value = 'failed'
  }
}

async function joinRoom() {
  const user = displayName.value.trim() || `${t('share.guestPrefix')}${Math.floor(Math.random() * 1000)}`
  displayName.value = user
  await connection.invoke('JoinShare', shareCode, user)
  connectState.value = 'connected'
  hadConnectedOnce.value = true
  showWsMask.value = false
}

async function disconnectHub() {
  if (!connection) return
  await connection.stop()
  connection = null
  connectState.value = 'disconnected'
}

function onMouseMove(event) {
  if (!connection || connection.state !== 'Connected') return
  if (!connection.connectionId) return
  if (!boardRef.value) return
  if (Date.now() - lastSentAt < 30) return

  const box = boardRef.value.getBoundingClientRect()
  const x = Math.max(0, Math.min(box.width, event.clientX - box.left))
  const y = Math.max(0, Math.min(box.height, event.clientY - box.top))
  lastSentAt = Date.now()
  void connection.invoke('UpdateCursor', shareCode, x, y)

  const list = [...onlineUsers.value]
  const idx = list.findIndex((item) => item.connectionId === connection.connectionId)
  if (idx >= 0) {
    list[idx] = { ...list[idx], x, y }
    onlineUsers.value = list
  }
}

async function broadcastSharedContent() {
  if (!canEditShared.value) return
  if (remoteApplying.value) return
  if (!connection || connection.state !== 'Connected') return
  if (!isLoaded.value) return
  try {
    await connection.invoke('UpdateSharedContent', shareCode, contentJson.value)
  } catch {
    // ignore realtime push errors, autosave will still persist content
  }
}

async function saveShared(force = true) {
  if (!canEditShared.value) return
  if (!isLoaded.value) return
  if (saveTimer && force) {
    clearTimeout(saveTimer)
    saveTimer = null
  }
  if (!force && contentJson.value === lastSavedContent.value) return
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
    if (force && mindmapRef.value) {
      contentJson.value = mindmapRef.value.getGraphJson()
    }
    await pbUpdateShared(shareCode, contentJson.value)
    lastSavedContent.value = contentJson.value
    messageKey.value = force ? 'share.saved' : 'share.autoSaved'
  } catch (err) {
    error.value = err.message
  } finally {
    saving.value = false
    if (saveQueued.value) {
      saveQueued.value = false
      void saveShared(false)
    }
  }
}

async function onCanvasOperation(payload) {
  if (!canEditShared.value) return
  if (!payload?.actionType) return
  try {
    await pbAddShareHistory(
      String(shareCode),
      String(payload.actionType || ''),
      JSON.stringify(payload.detail || {}),
      String(displayName.value || '')
    )
  } catch {
    // ignore history record errors
  }
}

function callCanvasAction(actionName) {
  const canvas = mindmapRef.value
  if (!canvas || typeof canvas[actionName] !== 'function') return
  canvas[actionName]()
}

async function backHome() {
  await router.push('/')
}
</script>

<template>
  <main class="share-full-page">
    <div ref="boardRef" class="share-board share-full-board" @mousemove="onMouseMove">
      <MindMapCanvas
        ref="mindmapRef"
        v-model="contentJson"
        :height="canvasHeight"
        :show-toolbar="false"
        :readonly="!canEditShared"
        @operation="onCanvasOperation"
      />

      <div class="share-top-left-bar">
        <button class="share-icon-btn" @click="backHome">←</button>
        <button class="share-icon-btn" @click.stop="toggleSettings">☰</button>
        <div class="share-doc-name">{{ mapTitle || shareCode }}</div>
        <span class="share-mode-chip">{{ shareModeLabel }}</span>
      </div>

      <div v-if="settingsOpen" ref="settingsRef" class="share-settings-panel">
        <div class="share-title">{{ shareDisplayTitle }}</div>
        <div v-if="canManageShare" class="share-access-row">
          <span class="share-access-label">{{ t('share.accessTitle') }}</span>
          <label class="share-access-check">
            <input v-model="shareEnabled" type="checkbox" />
            <span>{{ t('home.shareEnabled') }}</span>
          </label>
          <select v-model="shareRequireLogin" class="share-access-select">
            <option :value="false">{{ t('share.accessPublicOperate') }}</option>
            <option :value="true">{{ t('share.accessLoginOperate') }}</option>
          </select>
          <label class="share-access-check">
            <input v-model="shareAllowGuestEdit" type="checkbox" :disabled="shareRequireLogin || !shareEnabled" />
            <span>{{ t('home.shareGuestCanEdit') }}</span>
          </label>
          <button :disabled="accessSaving" @click="applyShareAccessMode">{{ t('share.applyAccess') }}</button>
        </div>
        <div class="actions">
          <button class="primary" :disabled="saving || !canEditShared" @click="saveShared(true)">
            {{ t('share.saveShared') }}
          </button>
          <button @click="backHome">{{ t('share.backHome') }}</button>
        </div>
      </div>

      <div class="share-top-right-bar">
        <button class="share-icon-btn" @click="callCanvasAction('fitView')">⌖</button>
        <div ref="onlineRef" class="share-online-wrap">
          <button class="share-pill-btn" @click.stop="toggleOnline">{{ t('share.onlineUsers') }} ({{ usersForList.length }})</button>
          <ul v-if="onlineOpen" class="share-online-menu">
            <li v-for="user in usersForList" :key="user.connectionId">
              <strong>
                {{ user.displayName }}
                <small v-if="user.connectionId === myConnectionId">({{ t('share.me') }})</small>
              </strong>
              <span v-if="user.connectionId !== myConnectionId">{{ Math.round(user.x || 0) }}, {{ Math.round(user.y || 0) }}</span>
              <span v-else>--</span>
            </li>
          </ul>
        </div>
        <div ref="menuRef" class="share-lang-wrap">
          <button class="share-pill-btn" @click.stop="toggleMenu">{{ t('app.langLabel') }}: {{ currentLocaleLabel }}</button>
          <div v-if="menuOpen" class="lang-menu">
            <button :class="{ active: currentLocale === 'zh' }" @click="switchLocale('zh')">{{ t('app.zh') }}</button>
            <button :class="{ active: currentLocale === 'en' }" @click="switchLocale('en')">{{ t('app.en') }}</button>
          </div>
        </div>
        <button class="primary share-save-btn" :disabled="saving || !canEditShared" @click="saveShared(true)">
          {{ t('share.saveShared') }}
        </button>
      </div>

      <div class="share-left-toolbox">
        <button class="share-side-btn" :disabled="!canEditShared" @click="callCanvasAction('addFreeNode')">＋</button>
        <button class="share-side-btn" :disabled="!canEditShared" @click="callCanvasAction('addChildNode')">子</button>
        <button class="share-side-btn" :disabled="!canEditShared" @click="callCanvasAction('addSiblingNode')">同</button>
        <button class="share-side-btn" @click="callCanvasAction('fitView')">适</button>
      </div>

      <div class="cursor-layer">
        <div
          v-for="user in usersForCursor"
          :key="user.connectionId"
          class="cursor"
          :style="{ left: `${user.x || 0}px`, top: `${user.y || 0}px` }"
        >
          <span>{{ user.displayName }}</span>
        </div>
      </div>

      <div v-if="showWsMask" class="ws-reconnect-mask">
        <div class="ws-reconnect-text">{{ wsMaskText }}</div>
      </div>

      <p v-if="message" class="ok share-float-msg">{{ message }}</p>
      <p v-if="error" class="err share-float-msg">{{ error }}</p>
    </div>
  </main>
</template>

