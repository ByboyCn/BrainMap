<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { setLocale } from './i18n'

const { t, locale } = useI18n()
const route = useRoute()

const currentLocale = computed(() => locale.value)
const currentLocaleLabel = computed(() => (currentLocale.value === 'zh' ? t('app.zh') : t('app.en')))
const showTopbar = computed(() => !String(route.path || '').startsWith('/share/'))
const menuOpen = ref(false)
const menuRef = ref(null)

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

onMounted(() => {
  document.addEventListener('click', handleDocumentClick)
})

onBeforeUnmount(() => {
  document.removeEventListener('click', handleDocumentClick)
})
</script>

<template>
  <header v-if="showTopbar" class="topbar">
    <div ref="menuRef" class="lang-switch dropdown">
      <button class="lang-trigger" @click.stop="toggleMenu">{{ t('app.langLabel') }}: {{ currentLocaleLabel }}</button>
      <div v-if="menuOpen" class="lang-menu">
        <button :class="{ active: currentLocale === 'zh' }" @click="switchLocale('zh')">{{ t('app.zh') }}</button>
        <button :class="{ active: currentLocale === 'en' }" @click="switchLocale('en')">{{ t('app.en') }}</button>
      </div>
    </div>
  </header>
  <router-view />
</template>
