<script setup>
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import MindMapCanvas from '../components/MindMapCanvas.vue'
import { setLocale } from '../i18n'
import { getApiBaseUrl, getSession } from '../services/api'
import { pbAddShareHistory, pbGetShareHistory, pbGetShared, pbUpdateShared } from '../services/pb'

const route = useRoute()
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
const hadConnectedOnce = ref(false)
const showWsMask = ref(false)
const defaultDocumentTitle = document.title
const shareHistory = ref([])
const historyLoading = ref(false)

let connection = null
let lastSentAt = 0
let saveTimer = null
let broadcastTimer = null
let historyTimer = null

const AUTO_SAVE_DELAY_MS = 700
const BROADCAST_DELAY_MS = 120
const message = computed(() => (messageKey.value ? t(messageKey.value) : ''))
const connectStateLabel = computed(() => t(`share.state.${connectState.value}`))
const autoSaveStatus = computed(() => (saving.value ? t('editor.saving') : t('editor.enabled')))
const myConnectionId = computed(() => connection?.connectionId || '')
const usersForList = computed(() => onlineUsers.value.filter((user) => !!user.connectionId))
const usersForCursor = computed(() =>
  onlineUsers.value.filter((user) => user.connectionId && user.connectionId !== myConnectionId.value)
)
const canvasHeight = computed(() => Math.max(420, viewportHeight.value))
const currentLocale = computed(() => locale.value)
const currentLocaleLabel = computed(() => (currentLocale.value === 'zh' ? t('app.zh') : t('app.en')))
const wsMaskText = computed(() => (currentLocale.value === 'en' ? 'WebSocket reconnecting...' : 'ws正在重新连接中'))

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
  if (historyTimer) {
    clearTimeout(historyTimer)
    historyTimer = null
  }
  document.title = defaultDocumentTitle
  await disconnectHub()
})

watch(contentJson, () => {
  if (!isLoaded.value || suppressWatch.value) return
  scheduleAutoSave()
  scheduleBroadcast()
})

watch(
  [mapTitle, () => String(shareCode || '')],
  ([title, code]) => {
    const safeTitle = String(title || '').trim()
    document.title = safeTitle || code || defaultDocumentTitle
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

function handleDocumentClick(event) {
  if (!menuRef.value) return
  if (!menuRef.value.contains(event.target)) {
    menuOpen.value = false
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
    mapTitle.value = payload.title
    suppressWatch.value = true
    contentJson.value = payload.contentJson || '{"nodes":[],"edges":[]}'
    lastSavedContent.value = contentJson.value
    isLoaded.value = true
    await loadShareHistory()
  } catch (err) {
    error.value = err.message
  } finally {
    suppressWatch.value = false
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
    scheduleHistoryReload()
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

function scheduleHistoryReload(delayMs = 350) {
  if (historyTimer) {
    clearTimeout(historyTimer)
  }
  historyTimer = setTimeout(() => {
    historyTimer = null
    void loadShareHistory()
  }, delayMs)
}

async function loadShareHistory() {
  if (!shareCode) return
  historyLoading.value = true
  try {
    shareHistory.value = await pbGetShareHistory(String(shareCode), 40)
  } catch {
    // ignore history fetch errors to avoid breaking collaboration flow
  } finally {
    historyLoading.value = false
  }
}

async function onCanvasOperation(payload) {
  if (!payload?.actionType) return
  try {
    await pbAddShareHistory(
      String(shareCode),
      String(payload.actionType || ''),
      JSON.stringify(payload.detail || {}),
      String(displayName.value || '')
    )
    scheduleHistoryReload(120)
  } catch {
    // ignore history record errors
  }
}

function getHistoryActionLabel(actionType) {
  switch (String(actionType || '').toLowerCase()) {
    case 'open':
      return t('share.historyAction.open')
    case 'node_add':
      return t('share.historyAction.nodeAdd')
    case 'node_delete':
      return t('share.historyAction.nodeDelete')
    case 'node_reparent':
      return t('share.historyAction.nodeReparent')
    default:
      return t('share.historyAction.unknown')
  }
}

function formatHistoryTime(unixMs) {
  const value = Number(unixMs || 0)
  if (!value) return '--'
  return new Date(value).toLocaleTimeString()
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
        @operation="onCanvasOperation"
      />

      <div class="share-floating-tools">
        <div class="share-title">{{ mapTitle || shareCode }}</div>
        <div class="share-meta">{{ t('share.signalr') }}: {{ connectStateLabel }} | {{ t('share.autoSave') }}: {{ autoSaveStatus }}</div>
        <div class="actions">
          <button class="primary" :disabled="saving" @click="saveShared(true)">{{ t('share.saveShared') }}</button>
          <a href="/">{{ t('share.backHome') }}</a>
        </div>
        <div class="share-history">
          <div class="share-history-title">{{ t('share.historyTitle') }}</div>
          <div v-if="historyLoading" class="share-history-loading">{{ t('share.historyLoading') }}</div>
          <ul v-else-if="shareHistory.length" class="share-history-list">
            <li v-for="item in shareHistory" :key="item.id">
              <span class="time">{{ formatHistoryTime(item.createdAtUnixMs) }}</span>
              <span class="user">{{ item.actorDisplayName || t('share.guestPrefix') }}</span>
              <span class="action">{{ getHistoryActionLabel(item.actionType) }}</span>
            </li>
          </ul>
          <div v-else class="share-history-empty">{{ t('share.historyEmpty') }}</div>
        </div>
      </div>

      <div class="online-hover-box">
        <div class="online-top-row">
          <div class="online-hover-trigger">{{ t('share.onlineUsers') }} ({{ usersForList.length }})</div>
          <div ref="menuRef" class="lang-switch dropdown share-lang-switch">
            <button class="lang-trigger" @click.stop="toggleMenu">{{ t('app.langLabel') }}: {{ currentLocaleLabel }}</button>
            <div v-if="menuOpen" class="lang-menu">
              <button :class="{ active: currentLocale === 'zh' }" @click="switchLocale('zh')">{{ t('app.zh') }}</button>
              <button :class="{ active: currentLocale === 'en' }" @click="switchLocale('en')">{{ t('app.en') }}</button>
            </div>
          </div>
        </div>
        <ul class="online-hover-menu">
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
