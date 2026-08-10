import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { createVuetify } from 'vuetify'
import 'vuetify/styles'
import App from './App.vue'
import { router } from './router'

createApp(App)
  .use(createPinia())
  .use(router)
  .use(createVuetify())
  .mount('#app')
