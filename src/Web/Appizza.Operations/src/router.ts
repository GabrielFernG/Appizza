import { createRouter, createWebHistory } from 'vue-router'
import FoundationView from './views/FoundationView.vue'
import KitchenQueueView from './views/KitchenQueueView.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [{ path: '/', name: 'foundation', component: FoundationView }, { path: '/kitchen', name: 'kitchen', component: KitchenQueueView }],
})
