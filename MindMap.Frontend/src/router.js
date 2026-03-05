import { createRouter, createWebHistory } from 'vue-router'
import HomeView from './views/HomeView.vue'
import ShareEntryView from './views/ShareEntryView.vue'
import EditorView from './views/EditorView.vue'
import TodoView from './views/TodoView.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: HomeView },
    { path: '/editor/:id', component: EditorView, props: true },
    { path: '/todo/:id', component: TodoView, props: true },
    { path: '/share/:shareCode', component: ShareEntryView, props: true },
  ],
})

export default router
