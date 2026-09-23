<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { AuthContext } from '../services/auth'
import { ClosingApi, type ClosingSession } from '../services/closingApi'
const api = new ClosingApi(new AuthContext()); const router = useRouter(); const sessions = ref<ClosingSession[]>([]); const loading = ref(false); const error = ref('')
async function load() { loading.value = true; error.value = ''; try { sessions.value = await api.closingSessions() } catch (e) { sessions.value = []; error.value = e instanceof Error ? e.message : 'Falha ao consultar fechamentos' } finally { loading.value = false } }
function open(sessionId: string) { void router.push({ name: 'closing-payments', query: { sessionId } }) }
onMounted(() => { void load() })
</script>
<template><v-container><h1>Sessões em fechamento</h1><v-btn @click="load" :loading="loading">Atualizar</v-btn><v-progress-circular v-if="loading" indeterminate /><v-alert v-if="error" type="warning">{{ error }}</v-alert><div v-if="!loading && !error && sessions.length === 0">Nenhuma sessão em fechamento</div><v-card v-for="session in sessions" :key="session.sessionId" class="mt-2"><v-card-text>Sessão {{ session.sessionId }}<br>Status: {{ session.status }}<br>Mesa: {{ session.diningTableId }}<br><v-btn @click="open(session.sessionId)">Abrir detalhes</v-btn></v-card-text></v-card></v-container></template>
