function getDefaultApiBaseUrl() {
  if (typeof window === 'undefined') {
    return 'http://localhost:5289'
  }
  const protocol = window.location.protocol === 'https:' ? 'https:' : 'http:'
  const host = window.location.hostname || 'localhost'
  return `${protocol}//${host}:5289`
}

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || getDefaultApiBaseUrl()
const STORAGE_KEY = 'mindmap_session'

export function getApiBaseUrl() {
  return API_BASE_URL
}

export function getSession() {
  const raw = localStorage.getItem(STORAGE_KEY)
  if (!raw) return null

  try {
    return JSON.parse(raw)
  } catch {
    return null
  }
}

export function saveSession(session) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(session))
}

export function clearSession() {
  localStorage.removeItem(STORAGE_KEY)
}

export async function apiFetch(path, { method = 'GET', body, auth = false } = {}) {
  const headers = { 'Content-Type': 'application/json' }
  const options = {
    method,
    headers,
  }

  if (body !== undefined) {
    options.body = JSON.stringify(body)
  }

  if (auth) {
    const session = getSession()
    if (session?.token) {
      options.headers.Authorization = `Bearer ${session.token}`
    }
  }

  const response = await fetch(`${API_BASE_URL}${path}`, options)
  const isJson = response.headers.get('content-type')?.includes('application/json')
  const payload = isJson ? await response.json() : null

  if (!response.ok) {
    throw new Error(payload?.message || `请求失败 (${response.status})`)
  }

  return payload
}
