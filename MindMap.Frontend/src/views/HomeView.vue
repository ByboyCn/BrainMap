<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { clearSession, getSession, saveSession } from '../services/api'
import {
  pbCreateMap,
  pbCreateTodo,
  pbDeleteMap,
  pbDeleteTodo,
  pbListMaps,
  pbListTodos,
  pbLogin,
  pbRegister,
} from '../services/pb'

const router = useRouter()
const { t } = useI18n()
const authMode = ref('login')
const authForm = reactive({
  userName: '',
  password: '',
})
const createType = ref('mindmap')
const createTitle = ref('')
const maps = ref([])
const todos = ref([])
const busy = ref(false)
const messageKey = ref('')
const error = ref('')
const session = ref(getSession())

const isAuthenticated = computed(() => !!session.value?.token)
const message = computed(() => (messageKey.value ? t(messageKey.value) : ''))
const createPlaceholder = computed(() =>
  createType.value === 'todo' ? t('home.newTodoTitle') : t('home.newMindmapTitle')
)

onMounted(async () => {
  if (isAuthenticated.value) {
    await loadAllDocuments()
  }
})

function resetStatus() {
  messageKey.value = ''
  error.value = ''
}

async function submitAuth() {
  resetStatus()
  busy.value = true
  try {
    const fn = authMode.value === 'register' ? pbRegister : pbLogin
    const result = await fn(authForm.userName, authForm.password)
    session.value = {
      userId: result.userId,
      userName: result.userName,
      token: result.token,
    }
    saveSession(session.value)
    authForm.password = ''
    messageKey.value = authMode.value === 'register' ? 'home.registerSuccess' : 'home.loginSuccess'
    await loadAllDocuments()
  } catch (err) {
    error.value = err.message
  } finally {
    busy.value = false
  }
}

function logout() {
  clearSession()
  session.value = null
  maps.value = []
  todos.value = []
  messageKey.value = 'home.logoutSuccess'
  error.value = ''
}

async function loadAllDocuments() {
  if (!isAuthenticated.value) return
  resetStatus()
  busy.value = true
  try {
    const [mindmapList, todoList] = await Promise.all([pbListMaps(), pbListTodos()])
    maps.value = mindmapList
    todos.value = todoList
  } catch (err) {
    error.value = err.message
  } finally {
    busy.value = false
  }
}

async function createDocument() {
  resetStatus()
  busy.value = true
  try {
    if (createType.value === 'todo') {
      const todo = await pbCreateTodo(createTitle.value, '{"docType":"todo","sortBy":"natural","items":[]}')
      createTitle.value = ''
      await loadAllDocuments()
      await router.push(`/todo/${todo.id}`)
      return
    }

    const map = await pbCreateMap(
      createTitle.value,
      '{"docType":"mindmap","nodes":[],"edges":[],"meta":{"backgroundColor":"#ffffff"}}'
    )
    createTitle.value = ''
    await loadAllDocuments()
    await router.push(`/editor/${map.id}`)
  } catch (err) {
    error.value = err.message
  } finally {
    busy.value = false
  }
}

async function openEditor(id) {
  await router.push(`/editor/${id}`)
}

async function openTodo(id) {
  await router.push(`/todo/${id}`)
}

async function deleteMap(id) {
  if (!id) return
  const ok = window.confirm(t('home.deleteMindmapConfirm'))
  if (!ok) return

  resetStatus()
  busy.value = true
  try {
    await pbDeleteMap(id)
    await loadAllDocuments()
  } catch (err) {
    error.value = err.message
  } finally {
    busy.value = false
  }
}

async function deleteTodo(id) {
  if (!id) return
  const ok = window.confirm(t('home.deleteTodoConfirm'))
  if (!ok) return

  resetStatus()
  busy.value = true
  try {
    await pbDeleteTodo(id)
    await loadAllDocuments()
  } catch (err) {
    error.value = err.message
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <main class="page">
    <section class="panel">
      <h1>{{ t('home.title') }}</h1>
      <p class="sub">{{ t('home.subtitle') }}</p>

      <div v-if="!isAuthenticated" class="auth">
        <div class="tabs">
          <button :class="{ active: authMode === 'login' }" @click="authMode = 'login'">{{ t('home.login') }}</button>
          <button :class="{ active: authMode === 'register' }" @click="authMode = 'register'">{{ t('home.register') }}</button>
        </div>
        <label>
          {{ t('home.username') }}
          <input v-model.trim="authForm.userName" :placeholder="t('home.inputUsername')" />
        </label>
        <label>
          {{ t('home.password') }}
          <input v-model="authForm.password" type="password" :placeholder="t('home.inputPassword')" />
        </label>
        <button class="primary" :disabled="busy" @click="submitAuth">
          {{ authMode === 'register' ? t('home.register') : t('home.login') }}
        </button>
      </div>

      <div v-else class="workspace">
        <div class="toolbar">
          <div>{{ t('home.user') }}: {{ session.userName }}</div>
          <div class="actions">
            <button @click="loadAllDocuments">{{ t('home.refresh') }}</button>
            <button @click="logout">{{ t('home.logout') }}</button>
          </div>
        </div>

        <div class="create-row">
          <select v-model="createType" class="create-type-select">
            <option value="mindmap">{{ t('home.createTypeMindmap') }}</option>
            <option value="todo">{{ t('home.createTypeTodo') }}</option>
          </select>
          <input v-model.trim="createTitle" :placeholder="createPlaceholder" />
          <button class="primary" :disabled="busy" @click="createDocument">{{ t('home.create') }}</button>
        </div>

        <section class="list-page">
          <h3>{{ t('home.myMindmaps') }}</h3>
          <div v-if="maps.length === 0" class="hint">{{ t('home.noMindmap') }}</div>
          <div v-else class="map-items">
            <div v-for="item in maps" :key="item.id" class="map-item">
              <div>
                <strong>{{ item.title }}</strong>
                <div class="hint">{{ new Date(item.updatedAtUnixMs).toLocaleString() }}</div>
              </div>
              <div class="actions">
                <button class="primary" @click="openEditor(item.id)">{{ t('home.openEditor') }}</button>
                <button class="danger" :disabled="busy" @click="deleteMap(item.id)">{{ t('home.delete') }}</button>
              </div>
            </div>
          </div>
        </section>

        <section class="list-page">
          <h3>{{ t('home.myTodos') }}</h3>
          <div v-if="todos.length === 0" class="hint">{{ t('home.noTodo') }}</div>
          <div v-else class="map-items">
            <div v-for="item in todos" :key="item.id" class="map-item">
              <div>
                <strong>{{ item.title }}</strong>
                <div class="hint">{{ new Date(item.updatedAtUnixMs).toLocaleString() }}</div>
              </div>
              <div class="actions">
                <button class="primary" @click="openTodo(item.id)">{{ t('home.openTodo') }}</button>
                <button class="danger" :disabled="busy" @click="deleteTodo(item.id)">{{ t('home.delete') }}</button>
              </div>
            </div>
          </div>
        </section>
      </div>

      <p v-if="message" class="ok">{{ message }}</p>
      <p v-if="error" class="err">{{ error }}</p>
    </section>
  </main>
</template>
