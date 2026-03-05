<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { getSession } from '../services/api'
import { pbGetTodo, pbUpdateTodo } from '../services/pb'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()

const session = ref(getSession())
const draftText = ref('')
const draftInputRef = ref(null)
const busy = ref(false)
const saving = ref(false)
const error = ref('')
const messageKey = ref('')
const isLoaded = ref(false)
const suppressWatch = ref(false)
const lastSavedSignature = ref('')
let saveTimer = null

const AUTO_SAVE_DELAY_MS = 600
const SORT_OPTIONS = ['natural', 'createdAt', 'plannedStartAt', 'plannedEndAt', 'startedAt', 'completedAt']

const todo = reactive({
  id: '',
  title: '',
  sortBy: 'natural',
  items: [],
})

const message = computed(() => (messageKey.value ? t(messageKey.value) : ''))
const autoSaveStatus = computed(() => (saving.value ? t('editor.saving') : t('editor.enabled')))

const sectionPending = computed(() => sortedItems.value.filter((item) => !item.completed && !item.archived))
const sectionDone = computed(() => sortedItems.value.filter((item) => item.completed && !item.archived))
const sectionArchivedPending = computed(() => sortedItems.value.filter((item) => !item.completed && item.archived))
const sectionArchivedDone = computed(() => sortedItems.value.filter((item) => item.completed && item.archived))

const sortedItems = computed(() => {
  const list = Array.isArray(todo.items) ? [...todo.items] : []
  const sortBy = SORT_OPTIONS.includes(todo.sortBy) ? todo.sortBy : 'natural'

  if (sortBy === 'natural') {
    return list.sort((a, b) => Number(a.order || 0) - Number(b.order || 0))
  }

  return list.sort((a, b) => {
    const av = Number(a?.[`${sortBy}UnixMs`] || 0)
    const bv = Number(b?.[`${sortBy}UnixMs`] || 0)
    if (av === bv) {
      return Number(a.order || 0) - Number(b.order || 0)
    }
    if (!av && bv) return 1
    if (av && !bv) return -1
    return av - bv
  })
})

onMounted(async () => {
  window.addEventListener('keydown', handleGlobalKeydown)
  if (!session.value?.token) {
    error.value = t('editor.loginFirst')
    return
  }
  await loadTodo()
})

onBeforeUnmount(() => {
  window.removeEventListener('keydown', handleGlobalKeydown)
  if (saveTimer) {
    clearTimeout(saveTimer)
    saveTimer = null
  }
})

watch(
  () => [todo.title, todo.sortBy, todo.items],
  () => {
    if (!isLoaded.value || suppressWatch.value) return
    scheduleAutoSave()
  },
  { deep: true }
)

function handleGlobalKeydown(event) {
  if (!event.ctrlKey || event.altKey || event.shiftKey || event.metaKey) return
  if (event.key !== '/') return
  event.preventDefault()
  draftInputRef.value?.focus()
}

function normalizeItem(raw, index) {
  const now = Date.now()
  return {
    id: String(raw?.id || `todo-item-${now}-${index}`),
    title: String(raw?.title || '').trim(),
    completed: !!raw?.completed,
    archived: !!raw?.archived,
    order: Number.isFinite(Number(raw?.order)) ? Number(raw.order) : index + 1,
    createdAtUnixMs: Number(raw?.createdAtUnixMs || now),
    plannedStartAtUnixMs: Number(raw?.plannedStartAtUnixMs || 0),
    plannedEndAtUnixMs: Number(raw?.plannedEndAtUnixMs || 0),
    startedAtUnixMs: Number(raw?.startedAtUnixMs || 0),
    completedAtUnixMs: Number(raw?.completedAtUnixMs || 0),
  }
}

function parseTodoContent(contentJson) {
  const fallback = {
    sortBy: 'natural',
    items: [],
  }

  try {
    const parsed = JSON.parse(contentJson || '{}')
    const sortBy = SORT_OPTIONS.includes(parsed?.sortBy) ? parsed.sortBy : fallback.sortBy
    const items = (Array.isArray(parsed?.items) ? parsed.items : []).map((item, index) => normalizeItem(item, index))
    return { sortBy, items }
  } catch {
    return fallback
  }
}

function buildTodoContentJson() {
  return JSON.stringify({
    docType: 'todo',
    sortBy: todo.sortBy,
    items: todo.items,
  })
}

function buildSignature() {
  return JSON.stringify({
    title: String(todo.title || '').trim(),
    contentJson: buildTodoContentJson(),
  })
}

function scheduleAutoSave() {
  if (saveTimer) {
    clearTimeout(saveTimer)
  }
  saveTimer = setTimeout(() => {
    saveTimer = null
    void saveTodo(false)
  }, AUTO_SAVE_DELAY_MS)
}

async function loadTodo() {
  busy.value = true
  error.value = ''
  messageKey.value = ''
  isLoaded.value = false

  try {
    const payload = await pbGetTodo(String(route.params.id || ''))
    const parsed = parseTodoContent(payload.contentJson)
    suppressWatch.value = true
    todo.id = payload.id
    todo.title = payload.title || t('todo.defaultTitle')
    todo.sortBy = parsed.sortBy
    todo.items = parsed.items
    lastSavedSignature.value = buildSignature()
    isLoaded.value = true
  } catch (err) {
    error.value = err.message
  } finally {
    suppressWatch.value = false
    busy.value = false
  }
}

async function saveTodo(force = true) {
  if (!todo.id || !isLoaded.value) return

  if (saveTimer && force) {
    clearTimeout(saveTimer)
    saveTimer = null
  }

  const signature = buildSignature()
  if (!force && signature === lastSavedSignature.value) return
  if (saving.value) return

  saving.value = true
  if (force) {
    messageKey.value = ''
  }
  error.value = ''

  try {
    const payload = await pbUpdateTodo(todo.id, todo.title, buildTodoContentJson())
    const parsed = parseTodoContent(payload.contentJson)
    suppressWatch.value = true
    todo.title = payload.title
    todo.sortBy = parsed.sortBy
    todo.items = parsed.items
    lastSavedSignature.value = buildSignature()
    messageKey.value = force ? 'share.saved' : 'share.autoSaved'
  } catch (err) {
    error.value = err.message
  } finally {
    suppressWatch.value = false
    saving.value = false
  }
}

function addItem() {
  const title = String(draftText.value || '').trim()
  if (!title) return

  const now = Date.now()
  const orderMax = todo.items.reduce((max, item) => Math.max(max, Number(item.order || 0)), 0)
  todo.items.push({
    id: `todo-item-${now}-${Math.floor(Math.random() * 10000)}`,
    title,
    completed: false,
    archived: false,
    order: orderMax + 1,
    createdAtUnixMs: now,
    plannedStartAtUnixMs: 0,
    plannedEndAtUnixMs: 0,
    startedAtUnixMs: 0,
    completedAtUnixMs: 0,
  })
  draftText.value = ''
}

function toggleCompleted(itemId) {
  const index = todo.items.findIndex((item) => item.id === itemId)
  if (index < 0) return

  const item = todo.items[index]
  const now = Date.now()
  const nextCompleted = !item.completed

  todo.items[index] = {
    ...item,
    completed: nextCompleted,
    startedAtUnixMs: nextCompleted ? (item.startedAtUnixMs || now) : 0,
    completedAtUnixMs: nextCompleted ? now : 0,
  }
}

function toggleArchived(itemId) {
  const index = todo.items.findIndex((item) => item.id === itemId)
  if (index < 0) return
  const item = todo.items[index]
  todo.items[index] = {
    ...item,
    archived: !item.archived,
  }
}

function removeItem(itemId) {
  todo.items = todo.items.filter((item) => item.id !== itemId)
}

async function backHome() {
  await router.push('/')
}
</script>

<template>
  <main class="todo-page">
    <section class="todo-shell">
      <div class="todo-head">
        <div class="todo-title-box">
          <button class="todo-back" @click="backHome">{{ t('share.backHome') }}</button>
          <input v-model.trim="todo.title" class="todo-title-input" :placeholder="t('todo.defaultTitle')" />
        </div>
        <select v-model="todo.sortBy" class="todo-sort-select">
          <option value="natural">{{ t('todo.sortNatural') }}</option>
          <option value="createdAt">{{ t('todo.sortCreatedAt') }}</option>
          <option value="plannedStartAt">{{ t('todo.sortPlannedStartAt') }}</option>
          <option value="plannedEndAt">{{ t('todo.sortPlannedEndAt') }}</option>
          <option value="startedAt">{{ t('todo.sortStartedAt') }}</option>
          <option value="completedAt">{{ t('todo.sortCompletedAt') }}</option>
        </select>
      </div>

      <div class="todo-body">
        <section class="todo-group">
          <div class="todo-group-label">{{ t('todo.groupPending') }}</div>
          <div v-for="item in sectionPending" :key="item.id" class="todo-item-row">
            <button class="todo-check" @click="toggleCompleted(item.id)" />
            <div class="todo-item-title">{{ item.title }}</div>
            <div class="todo-item-actions">
              <button class="todo-mini" @click="toggleArchived(item.id)">{{ t('todo.archive') }}</button>
              <button class="todo-mini danger" @click="removeItem(item.id)">{{ t('home.delete') }}</button>
            </div>
          </div>
        </section>

        <section class="todo-group">
          <div class="todo-group-label">{{ t('todo.groupDone') }}</div>
          <div v-for="item in sectionDone" :key="item.id" class="todo-item-row done">
            <button class="todo-check checked" @click="toggleCompleted(item.id)">✓</button>
            <div class="todo-item-title">{{ item.title }}</div>
            <div class="todo-item-actions">
              <button class="todo-mini" @click="toggleArchived(item.id)">{{ t('todo.archive') }}</button>
              <button class="todo-mini danger" @click="removeItem(item.id)">{{ t('home.delete') }}</button>
            </div>
          </div>
        </section>

        <section class="todo-group">
          <div class="todo-group-label">{{ t('todo.groupArchivedPending') }}</div>
          <div v-for="item in sectionArchivedPending" :key="item.id" class="todo-item-row">
            <button class="todo-check" @click="toggleCompleted(item.id)" />
            <div class="todo-item-title">{{ item.title }}</div>
            <div class="todo-item-actions">
              <button class="todo-mini" @click="toggleArchived(item.id)">{{ t('todo.unarchive') }}</button>
              <button class="todo-mini danger" @click="removeItem(item.id)">{{ t('home.delete') }}</button>
            </div>
          </div>
        </section>

        <section class="todo-group">
          <div class="todo-group-label">{{ t('todo.groupArchivedDone') }}</div>
          <div v-for="item in sectionArchivedDone" :key="item.id" class="todo-item-row done">
            <button class="todo-check checked" @click="toggleCompleted(item.id)">✓</button>
            <div class="todo-item-title">{{ item.title }}</div>
            <div class="todo-item-actions">
              <button class="todo-mini" @click="toggleArchived(item.id)">{{ t('todo.unarchive') }}</button>
              <button class="todo-mini danger" @click="removeItem(item.id)">{{ t('home.delete') }}</button>
            </div>
          </div>
        </section>
      </div>

      <div class="todo-editor-bar">
        <input
          ref="draftInputRef"
          v-model="draftText"
          class="todo-editor-input"
          :placeholder="t('todo.quickInputPlaceholder')"
          @keydown.enter.prevent="addItem"
        />
        <button class="primary todo-add-btn" :disabled="busy" @click="addItem">{{ t('todo.add') }}</button>
      </div>

      <p class="hint todo-status">{{ t('editor.autoSave') }}: {{ autoSaveStatus }}</p>
      <p v-if="message" class="ok">{{ message }}</p>
      <p v-if="error" class="err">{{ error }}</p>
    </section>
  </main>
</template>
