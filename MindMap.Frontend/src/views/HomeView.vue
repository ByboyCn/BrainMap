<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { clearSession, getSession, saveSession } from '../services/api'
import { pbCreateMap, pbDeleteMap, pbListMaps, pbLogin, pbRegister } from '../services/pb'

const router = useRouter()
const { t } = useI18n()
const authMode = ref('login')
const authForm = reactive({
  userName: '',
  password: '',
})
const createTitle = ref('')
const maps = ref([])
const busy = ref(false)
const messageKey = ref('')
const error = ref('')
const session = ref(getSession())

const isAuthenticated = computed(() => !!session.value?.token)
const message = computed(() => (messageKey.value ? t(messageKey.value) : ''))

onMounted(async () => {
  if (isAuthenticated.value) {
    await loadMaps()
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
    await loadMaps()
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
  messageKey.value = 'home.logoutSuccess'
  error.value = ''
}

async function loadMaps() {
  if (!isAuthenticated.value) return
  resetStatus()
  busy.value = true
  try {
    maps.value = await pbListMaps()
  } catch (err) {
    error.value = err.message
  } finally {
    busy.value = false
  }
}

async function createMap() {
  resetStatus()
  busy.value = true
  try {
    const map = await pbCreateMap(createTitle.value, '{"nodes":[],"edges":[],"meta":{"backgroundColor":"#ffffff"}}')
    createTitle.value = ''
    await loadMaps()
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

async function deleteMap(id) {
  if (!id) return
  const ok = window.confirm('确认删除这张脑图吗？')
  if (!ok) return

  resetStatus()
  busy.value = true
  try {
    await pbDeleteMap(id)
    messageKey.value = ''
    await loadMaps()
    window.alert('删除成功')
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
      <h1>脑图工作台</h1>
      <p class="sub">{{ t('home.subtitle') }}</p>

      <div v-if="!isAuthenticated" class="auth">
        <div class="tabs">
          <button :class="{ active: authMode === 'login' }" @click="authMode = 'login'">脑图登录</button>
          <button :class="{ active: authMode === 'register' }" @click="authMode = 'register'">脑图注册</button>
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
          {{ authMode === 'register' ? '脑图注册' : '脑图登录' }}
        </button>
      </div>

      <div v-else class="workspace">
        <div class="toolbar">
          <div>{{ t('home.user') }}: {{ session.userName }}</div>
          <div class="actions">
            <button @click="loadMaps">{{ t('home.refresh') }}</button>
            <button @click="logout">{{ t('home.logout') }}</button>
          </div>
        </div>

        <div class="create-row">
          <input v-model.trim="createTitle" :placeholder="t('home.newMindmapTitle')" />
          <button class="primary" :disabled="busy" @click="createMap">{{ t('home.create') }}</button>
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
                <button class="danger" :disabled="busy" @click="deleteMap(item.id)">删除</button>
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
