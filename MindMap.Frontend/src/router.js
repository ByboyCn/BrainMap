import { createRouter, createWebHistory } from 'vue-router'
import HomeView from './views/HomeView.vue'
import ShareView from './views/ShareView.vue'
import EditorView from './views/EditorView.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: HomeView },
    { path: '/editor/:id', component: EditorView, props: true },
    { path: '/share/:shareCode', component: ShareView, props: true },
  ],
})

export default router
