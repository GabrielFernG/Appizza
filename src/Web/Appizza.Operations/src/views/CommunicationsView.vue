<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import type { Communication, MediaAsset } from '../models/communication'
import { AuthContext } from '../services/auth'
import { CommunicationsApi, type CommunicationInput } from '../services/communicationsApi'
import { IntentKey } from '../services/idempotency'
import { ApiProblem } from '../services/problemDetails'
import { OperationsRealtime } from '../services/realtime'

const auth = new AuthContext()
const api = new CommunicationsApi(auth)
const intents = new IntentKey()
const items = ref<Communication[]>([])
const media = ref<MediaAsset[]>([])
const loading = ref(false); const creating = ref(false); const error = ref(''); const selected = ref<Record<string, boolean>>({})
const form = ref<CommunicationInput>({ title: '', body: '', mediaAssetId: null, mediaType: 'image', priority: 0, startsAt: new Date().toISOString().slice(0, 16), endsAt: new Date(Date.now() + 86400000).toISOString().slice(0, 16) })
let realtime: OperationsRealtime | undefined
let refreshPending = false
const has = (permission: string) => auth.has(permission)
const readyMedia = () => media.value.filter(x => x.status === 'ready' && (form.value.mediaType === 'image' ? x.mimeType.startsWith('image/') : x.mimeType.startsWith('video/')))
function message(reason: unknown) { if (reason instanceof ApiProblem) { if (reason.problem.status === 403) return 'Você não possui permissão para esta ação.'; if (reason.problem.status === 409) return 'O estado mudou. A lista foi atualizada.'; if (reason.problem.status === 404) return 'Recurso não encontrado.'; return reason.problem.detail ?? reason.problem.title ?? 'Requisição recusada.' } return 'Não foi possível concluir a operação.' }
async function refresh() { loading.value = true; error.value = ''; try { if (!auth.user) await api.me(); [items.value, media.value] = await Promise.all([api.list(), api.media()]) } catch (reason) { error.value = message(reason) } finally { loading.value = false } }
async function create() { if (creating.value || !form.value.title || new Date(form.value.endsAt) <= new Date(form.value.startsAt)) return; creating.value = true; error.value = ''; try { await api.create(form.value, intents.for('communications.create')); intents.clear('communications.create'); form.value = { ...form.value, title: '', body: '', mediaAssetId: null }; await refresh() } catch (reason) { error.value = message(reason); await refresh() } finally { creating.value = false } }
async function transition(item: Communication, action: 'publish' | 'pause' | 'archive') { if (selected.value[item.id]) return; selected.value = { ...selected.value, [item.id]: true }; try { await api.transition(item, action, intents.for(`communications.${action}:${item.id}`)); intents.clear(`communications.${action}:${item.id}`); await refresh() } catch (reason) { error.value = message(reason); await refresh() } finally { selected.value = { ...selected.value, [item.id]: false } } }
function invalidate() { if (!refreshPending) { refreshPending = true; queueMicrotask(() => { refreshPending = false; void refresh() }) } }
onMounted(async () => { await refresh(); realtime = new OperationsRealtime(() => auth.token, invalidate, invalidate); try { await realtime.start() } catch { /* GET remains available */ } })
onBeforeUnmount(() => { void realtime?.stop() })
</script>
<template>
  <v-container>
    <v-card title="Communications">
      <v-progress-linear v-if="loading" indeterminate />
      <v-alert v-if="error" type="error">{{ error }}</v-alert>
      <v-card-text v-if="has('communications.create')">
        <v-text-field v-model="form.title" label="Título" required />
        <v-textarea v-model="form.body" label="Texto" />
        <v-select v-model="form.mediaType" :items="['image', 'video']" label="Tipo de mídia" />
        <v-select v-model="form.mediaAssetId" :items="readyMedia()" item-title="fileName" item-value="id" label="Mídia" clearable />
        <v-text-field v-model.number="form.priority" type="number" label="Prioridade" />
        <v-text-field v-model="form.startsAt" type="datetime-local" label="Início" />
        <v-text-field v-model="form.endsAt" type="datetime-local" label="Fim" />
        <v-btn :loading="creating" :disabled="creating" @click="create">Criar rascunho</v-btn>
      </v-card-text>
      <v-alert v-if="!loading && !items.length" type="info">Nenhuma comunicação.</v-alert>
      <v-list>
        <v-list-item v-for="item in items" :key="item.id" :title="item.title" :subtitle="`${item.status} · prioridade ${item.priority}`">
          <template #append>
            <v-progress-circular v-if="selected[item.id]" indeterminate size="22" />
            <template v-else>
              <v-btn v-if="(item.status === 'draft' || item.status === 'paused') && has('communications.publish')" @click="transition(item, 'publish')">Publicar</v-btn>
              <v-btn v-if="item.status === 'published' && has('communications.edit')" @click="transition(item, 'pause')">Pausar</v-btn>
              <v-btn v-if="item.status !== 'archived' && has('communications.edit')" @click="transition(item, 'archive')">Arquivar</v-btn>
            </template>
          </template>
        </v-list-item>
      </v-list>
    </v-card>
  </v-container>
</template>
