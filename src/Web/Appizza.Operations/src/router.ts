import { createRouter, createWebHistory } from 'vue-router'
import FoundationView from './views/FoundationView.vue'
import KitchenQueueView from './views/KitchenQueueView.vue'
import CommunicationsView from './views/CommunicationsView.vue'
import ClosingPaymentsView from './views/ClosingPaymentsView.vue'
import ClosingSessionsView from './views/ClosingSessionsView.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [{ path: '/', name: 'foundation', component: FoundationView }, { path: '/kitchen', name: 'kitchen', component: KitchenQueueView }, { path: '/communications', name: 'communications', component: CommunicationsView }, { path: '/closing-sessions', name: 'closing-sessions', component: ClosingSessionsView }, { path: '/closing-payments', name: 'closing-payments', component: ClosingPaymentsView }],
})
