<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { setLocale } from '../i18n'
import { getSession } from '../services/api'
import { pbGetTodo, pbUpdateTodo } from '../services/pb'

const route = useRoute()
const router = useRouter()
const { t, locale } = useI18n()

const session = ref(getSession())
const draftText = ref('')
const draftInputRef = ref(null)
const subtaskDraft = ref('')
const selectedItemId = ref('')
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
const LABEL_OPTIONS = ['none', 'work', 'bug', 'idea']

const todo = reactive({
  id: '',
  title: '',
  sortBy: 'natural',
  items: [],
})

const message = computed(() => (messageKey.value ? t(messageKey.value) : ''))
const autoSaveStatus = computed(() => (saving.value ? t('editor.saving') : t('editor.enabled')))
const currentLocale = computed({
  get: () => (locale.value === 'en' ? 'en' : 'zh'),
  set: (nextLocale) => setLocale(nextLocale === 'en' ? 'en' : 'zh'),
})
const selectedItem = computed(() => todo.items.find((item) => item.id === selectedItemId.value) || null)
const labelOptions = computed(() =>
  LABEL_OPTIONS.map((value) => ({
    value,
    label: t(`todo.label${value.charAt(0).toUpperCase()}${value.slice(1)}`),
  }))
)

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

watch(
  () => todo.items.map((item) => item.id).join(','),
  () => {
    if (!selectedItemId.value) return
    const exists = todo.items.some((item) => item.id === selectedItemId.value)
    if (!exists) {
      selectedItemId.value = ''
    }
  }
)

function handleGlobalKeydown(event) {
  if (!event.ctrlKey || event.altKey || event.shiftKey || event.metaKey) return
  if (event.key !== '/') return
  event.preventDefault()
  draftInputRef.value?.focus()
}

function normalizeSubtask(raw, index, base) {
  return {
    id: String(raw?.id || `sub-${base}-${index}`),
    title: String(raw?.title || '').trim(),
    done: !!raw?.done,
  }
}

function normalizeItem(raw, index) {
  const now = Date.now()
  const base = String(raw?.id || `todo-item-${now}-${index}`)
  const label = LABEL_OPTIONS.includes(String(raw?.label || '').toLowerCase()) ? String(raw?.label).toLowerCase() : 'none'
  const subtasks = (Array.isArray(raw?.subtasks) ? raw.subtasks : []).map((subtask, subIndex) => normalizeSubtask(subtask, subIndex, base))

  return {
    id: base,
    title: String(raw?.title || '').trim(),
    completed: !!raw?.completed,
    archived: !!raw?.archived,
    label,
    note: String(raw?.note || '').slice(0, 200),
    subtasks,
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

function patchItem(itemId, patcher) {
  const index = todo.items.findIndex((item) => item.id === itemId)
  if (index < 0) return
  const current = todo.items[index]
  const next = typeof patcher === 'function' ? patcher(current) : { ...current, ...patcher }
  todo.items[index] = {
    ...current,
    ...next,
  }
}

function toDateInputValue(unixMs) {
  if (!unixMs) return ''
  const date = new Date(unixMs)
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

function fromDateInputValue(dateString) {
  if (!dateString) return 0
  const [y, m, d] = String(dateString).split('-').map((value) => Number(value))
  if (!y || !m || !d) return 0
  return new Date(y, m - 1, d).getTime()
}

function formatDateTime(unixMs) {
  if (!unixMs) return '--'
  return new Date(unixMs).toLocaleString()
}

function getSubtaskSummary(item) {
  const list = Array.isArray(item?.subtasks) ? item.subtasks : []
  if (list.length === 0) return ''
  const done = list.filter((subtask) => subtask.done).length
  const pending = list.length - done
  return `(${t('todo.subtaskDone')}:${done}, ${t('todo.subtaskPending')}:${pending}, ${t('todo.subtaskTotal')}:${list.length})`
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
    selectedItemId.value = ''
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

function selectItem(itemId) {
  selectedItemId.value = selectedItemId.value === itemId ? '' : itemId
}

function addItem() {
  const title = String(draftText.value || '').trim()
  if (!title) return

  const now = Date.now()
  const orderMax = todo.items.reduce((max, item) => Math.max(max, Number(item.order || 0)), 0)
  const itemId = `todo-item-${now}-${Math.floor(Math.random() * 10000)}`
  todo.items.push({
    id: itemId,
    title,
    completed: false,
    archived: false,
    label: 'none',
    note: '',
    subtasks: [],
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
  patchItem(itemId, (item) => ({
    archived: !item.archived,
  }))
}

function removeItem(itemId) {
  todo.items = todo.items.filter((item) => item.id !== itemId)
  if (selectedItemId.value === itemId) {
    selectedItemId.value = ''
  }
}

function updateSelectedTitle(value) {
  const itemId = selectedItemId.value
  if (!itemId) return
  patchItem(itemId, { title: String(value || '') })
}

function updateSelectedLabel(value) {
  const itemId = selectedItemId.value
  if (!itemId) return
  patchItem(itemId, { label: LABEL_OPTIONS.includes(value) ? value : 'none' })
}

function updateSelectedNote(value) {
  const itemId = selectedItemId.value
  if (!itemId) return
  patchItem(itemId, { note: String(value || '').slice(0, 200) })
}

function updateSelectedDate(fieldName, dateString) {
  const itemId = selectedItemId.value
  if (!itemId) return
  patchItem(itemId, { [fieldName]: fromDateInputValue(dateString) })
}

function addSubtask() {
  const item = selectedItem.value
  if (!item) return
  const title = String(subtaskDraft.value || '').trim()
  if (!title) return

  patchItem(item.id, {
    subtasks: [
      ...item.subtasks,
      {
        id: `sub-${Date.now()}-${Math.floor(Math.random() * 10000)}`,
        title,
        done: false,
      },
    ],
  })
  subtaskDraft.value = ''
}

function toggleSubtask(subtaskId) {
  const item = selectedItem.value
  if (!item) return
  toggleSubtaskByItem(item.id, subtaskId)
}

function toggleSubtaskByItem(itemId, subtaskId) {
  const item = todo.items.find((task) => task.id === itemId)
  if (!item) return
  patchItem(item.id, {
    subtasks: item.subtasks.map((subtask) =>
      subtask.id === subtaskId ? { ...subtask, done: !subtask.done } : subtask
    ),
  })
}

function removeSubtask(subtaskId) {
  const item = selectedItem.value
  if (!item) return
  patchItem(item.id, {
    subtasks: item.subtasks.filter((subtask) => subtask.id !== subtaskId),
  })
}

function toggleSelectedCompleted() {
  if (!selectedItem.value) return
  toggleCompleted(selectedItem.value.id)
}

function toggleSelectedArchived() {
  if (!selectedItem.value) return
  toggleArchived(selectedItem.value.id)
}

function removeSelectedItem() {
  if (!selectedItem.value) return
  removeItem(selectedItem.value.id)
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
        <div class="todo-head-actions">
          <select v-model="todo.sortBy" class="todo-sort-select">
            <option value="natural">{{ t('todo.sortNatural') }}</option>
            <option value="createdAt">{{ t('todo.sortCreatedAt') }}</option>
            <option value="plannedStartAt">{{ t('todo.sortPlannedStartAt') }}</option>
            <option value="plannedEndAt">{{ t('todo.sortPlannedEndAt') }}</option>
            <option value="startedAt">{{ t('todo.sortStartedAt') }}</option>
            <option value="completedAt">{{ t('todo.sortCompletedAt') }}</option>
          </select>
          <select v-model="currentLocale" class="todo-lang-select">
            <option value="zh">{{ t('app.zh') }}</option>
            <option value="en">{{ t('app.en') }}</option>
          </select>
        </div>
      </div>

      <div class="todo-content" :class="{ 'has-detail': !!selectedItem }">
        <section class="todo-list-pane" :class="{ 'with-detail': !!selectedItem }">
          <div class="todo-body" @click.self="selectedItemId = ''">
            <section class="todo-group">
              <div class="todo-group-label">{{ t('todo.groupPending') }}</div>
              <div
                v-for="item in sectionPending"
                :key="item.id"
                class="todo-item-row"
                :class="{ active: item.id === selectedItemId }"
                @click="selectItem(item.id)"
              >
                <button class="todo-check" @click.stop="toggleCompleted(item.id)" />
                <div class="todo-item-main">
                  <div class="todo-item-title">{{ item.title }}</div>
                  <div v-if="item.subtasks.length" class="todo-item-sub">{{ getSubtaskSummary(item) }}</div>
                  <div v-if="item.subtasks.length" class="todo-inline-subtasks">
                    <div
                      v-for="subtask in item.subtasks"
                      :key="subtask.id"
                      class="todo-inline-subtask-row"
                    >
                      <button
                        class="todo-inline-subtask-check"
                        :class="{ checked: subtask.done }"
                        @click.stop="toggleSubtaskByItem(item.id, subtask.id)"
                      >
                        {{ subtask.done ? '✓' : '' }}
                      </button>
                      <span class="todo-inline-subtask-text" :class="{ done: subtask.done }">{{ subtask.title }}</span>
                    </div>
                  </div>
                </div>
                <div class="todo-item-actions">
                  <button class="todo-mini" @click.stop="toggleArchived(item.id)">{{ t('todo.archive') }}</button>
                  <button class="todo-mini danger" @click.stop="removeItem(item.id)">{{ t('home.delete') }}</button>
                </div>
              </div>
            </section>

            <section class="todo-group">
              <div class="todo-group-label">{{ t('todo.groupDone') }}</div>
              <div
                v-for="item in sectionDone"
                :key="item.id"
                class="todo-item-row done"
                :class="{ active: item.id === selectedItemId }"
                @click="selectItem(item.id)"
              >
                <button class="todo-check checked" @click.stop="toggleCompleted(item.id)">✓</button>
                <div class="todo-item-main">
                  <div class="todo-item-title">{{ item.title }}</div>
                  <div v-if="item.subtasks.length" class="todo-item-sub">{{ getSubtaskSummary(item) }}</div>
                  <div v-if="item.subtasks.length" class="todo-inline-subtasks">
                    <div
                      v-for="subtask in item.subtasks"
                      :key="subtask.id"
                      class="todo-inline-subtask-row"
                    >
                      <button
                        class="todo-inline-subtask-check"
                        :class="{ checked: subtask.done }"
                        @click.stop="toggleSubtaskByItem(item.id, subtask.id)"
                      >
                        {{ subtask.done ? '✓' : '' }}
                      </button>
                      <span class="todo-inline-subtask-text" :class="{ done: subtask.done }">{{ subtask.title }}</span>
                    </div>
                  </div>
                </div>
                <div class="todo-item-actions">
                  <button class="todo-mini" @click.stop="toggleArchived(item.id)">{{ t('todo.archive') }}</button>
                  <button class="todo-mini danger" @click.stop="removeItem(item.id)">{{ t('home.delete') }}</button>
                </div>
              </div>
            </section>

            <section class="todo-group">
              <div class="todo-group-label">{{ t('todo.groupArchivedPending') }}</div>
              <div
                v-for="item in sectionArchivedPending"
                :key="item.id"
                class="todo-item-row"
                :class="{ active: item.id === selectedItemId }"
                @click="selectItem(item.id)"
              >
                <button class="todo-check" @click.stop="toggleCompleted(item.id)" />
                <div class="todo-item-main">
                  <div class="todo-item-title">{{ item.title }}</div>
                  <div v-if="item.subtasks.length" class="todo-item-sub">{{ getSubtaskSummary(item) }}</div>
                  <div v-if="item.subtasks.length" class="todo-inline-subtasks">
                    <div
                      v-for="subtask in item.subtasks"
                      :key="subtask.id"
                      class="todo-inline-subtask-row"
                    >
                      <button
                        class="todo-inline-subtask-check"
                        :class="{ checked: subtask.done }"
                        @click.stop="toggleSubtaskByItem(item.id, subtask.id)"
                      >
                        {{ subtask.done ? '✓' : '' }}
                      </button>
                      <span class="todo-inline-subtask-text" :class="{ done: subtask.done }">{{ subtask.title }}</span>
                    </div>
                  </div>
                </div>
                <div class="todo-item-actions">
                  <button class="todo-mini" @click.stop="toggleArchived(item.id)">{{ t('todo.unarchive') }}</button>
                  <button class="todo-mini danger" @click.stop="removeItem(item.id)">{{ t('home.delete') }}</button>
                </div>
              </div>
            </section>

            <section class="todo-group">
              <div class="todo-group-label">{{ t('todo.groupArchivedDone') }}</div>
              <div
                v-for="item in sectionArchivedDone"
                :key="item.id"
                class="todo-item-row done"
                :class="{ active: item.id === selectedItemId }"
                @click="selectItem(item.id)"
              >
                <button class="todo-check checked" @click.stop="toggleCompleted(item.id)">✓</button>
                <div class="todo-item-main">
                  <div class="todo-item-title">{{ item.title }}</div>
                  <div v-if="item.subtasks.length" class="todo-item-sub">{{ getSubtaskSummary(item) }}</div>
                  <div v-if="item.subtasks.length" class="todo-inline-subtasks">
                    <div
                      v-for="subtask in item.subtasks"
                      :key="subtask.id"
                      class="todo-inline-subtask-row"
                    >
                      <button
                        class="todo-inline-subtask-check"
                        :class="{ checked: subtask.done }"
                        @click.stop="toggleSubtaskByItem(item.id, subtask.id)"
                      >
                        {{ subtask.done ? '✓' : '' }}
                      </button>
                      <span class="todo-inline-subtask-text" :class="{ done: subtask.done }">{{ subtask.title }}</span>
                    </div>
                  </div>
                </div>
                <div class="todo-item-actions">
                  <button class="todo-mini" @click.stop="toggleArchived(item.id)">{{ t('todo.unarchive') }}</button>
                  <button class="todo-mini danger" @click.stop="removeItem(item.id)">{{ t('home.delete') }}</button>
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

        <aside v-if="selectedItem" class="todo-detail-pane">
          <h3>{{ t('todo.detailTitle') }}</h3>

          <label>
            {{ t('todo.fieldTitle') }}
            <input :value="selectedItem.title" @input="updateSelectedTitle($event.target.value)" />
          </label>

          <label>
            {{ t('todo.fieldLabel') }}
            <select :value="selectedItem.label" @change="updateSelectedLabel($event.target.value)">
              <option v-for="option in labelOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
            </select>
          </label>

          <div class="todo-subtask-card">
            <div class="todo-detail-label">{{ t('todo.subtasks') }}</div>
            <div v-for="subtask in selectedItem.subtasks" :key="subtask.id" class="todo-subtask-row">
              <button class="todo-subtask-check" :class="{ checked: subtask.done }" @click="toggleSubtask(subtask.id)">
                {{ subtask.done ? '✓' : '' }}
              </button>
              <span class="todo-subtask-text" :class="{ done: subtask.done }">{{ subtask.title }}</span>
              <button class="todo-mini danger" @click="removeSubtask(subtask.id)">{{ t('home.delete') }}</button>
            </div>
            <div class="todo-subtask-add-row">
              <input v-model.trim="subtaskDraft" :placeholder="t('todo.addSubtask')" @keydown.enter.prevent="addSubtask" />
              <button class="todo-mini" @click="addSubtask">{{ t('todo.add') }}</button>
            </div>
          </div>

          <label>
            {{ t('todo.startTime') }}
            <input
              type="date"
              :value="toDateInputValue(selectedItem.startedAtUnixMs)"
              @change="updateSelectedDate('startedAtUnixMs', $event.target.value)"
            />
          </label>

          <label>
            {{ t('todo.completeTime') }}
            <input
              type="date"
              :value="toDateInputValue(selectedItem.completedAtUnixMs)"
              @change="updateSelectedDate('completedAtUnixMs', $event.target.value)"
            />
          </label>

          <label>
            {{ t('todo.plannedStartTime') }}
            <input
              type="date"
              :value="toDateInputValue(selectedItem.plannedStartAtUnixMs)"
              @change="updateSelectedDate('plannedStartAtUnixMs', $event.target.value)"
            />
          </label>

          <label>
            {{ t('todo.plannedEndTime') }}
            <input
              type="date"
              :value="toDateInputValue(selectedItem.plannedEndAtUnixMs)"
              @change="updateSelectedDate('plannedEndAtUnixMs', $event.target.value)"
            />
          </label>

          <label>
            {{ t('todo.note') }}
            <textarea
              rows="4"
              :value="selectedItem.note"
              maxlength="200"
              @input="updateSelectedNote($event.target.value)"
            />
            <div class="todo-note-count">{{ selectedItem.note.length }}/200</div>
          </label>

          <div class="todo-detail-actions">
            <button class="primary" @click="toggleSelectedCompleted">
              {{ selectedItem.completed ? t('todo.markPending') : t('todo.markCompleted') }}
            </button>
            <button @click="toggleSelectedArchived">
              {{ selectedItem.archived ? t('todo.moveOutArchive') : t('todo.moveToArchive') }}
            </button>
            <button class="danger" @click="removeSelectedItem">{{ t('todo.deleteTask') }}</button>
          </div>

          <div class="todo-created-at">{{ t('todo.createdAt') }}: {{ formatDateTime(selectedItem.createdAtUnixMs) }}</div>
        </aside>
      </div>
    </section>
  </main>
</template>
