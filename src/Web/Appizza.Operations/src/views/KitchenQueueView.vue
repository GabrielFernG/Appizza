<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { KitchenDeliveryItem } from '../models/delivery'
import { AuthContext } from '../services/auth'
import { IntentKey } from '../services/idempotency'
import { KitchenApi } from '../services/kitchenApi'
import { OperationsRealtime } from '../services/realtime'
import { ApiProblem } from '../services/problemDetails'

const auth = new AuthContext()
const api = new KitchenApi(auth)
const intents = new IntentKey()
const token = ref(auth.token ?? '')
const stations = ref<Array<{ id: string; name: string }>>([])
const items = ref<KitchenDeliveryItem[]>([])
const selectedStation = ref('')
const selected = ref<KitchenDeliveryItem | null>(null)
const error = ref('')
const busy = ref(new Set<string>())
let poll: ReturnType<typeof setInterval> | undefined
let realtime: OperationsRealtime | undefined
let refreshPending = false

const ordered = computed(() => [...items.value].sort((a, b) => a.queuePosition - b.queuePosition))
const has = (permission: string) => auth.has(permission)
const label = (status: string, attention: boolean) => attention ? 'Entrega contestada' : ({ ready: 'Pronto para envio', awaiting_delivery_confirmation: 'Aguardando confirmação', delivered: 'Entregue', awaiting_acceptance: 'Aguardando aceite', preparing: 'Em preparo' }[status] ?? status)
const isBusy = (id: string) => busy.value.has(id)
function setBusy(id: string, value: boolean) { const next = new Set(busy.value); if (value) next.add(id); else next.delete(id); busy.value = next }
function problemMessage(reason: unknown): string {
  if (reason instanceof ApiProblem) {
    if (reason.problem.status === 403) return 'Você não possui permissão para esta ação.'
    if (reason.problem.status === 409) return 'O estado foi alterado por outro operador. A fila foi atualizada.'
    if (reason.problem.errorCode === 'DELIVERY_ALREADY_CONFIRMED') return 'A entrega já foi confirmada. A fila foi atualizada.'
    if (reason.problem.errorCode === 'DELIVERY_CONTEST_ALREADY_RESOLVED') return 'A contestação já foi resolvida. A fila foi atualizada.'
    return reason.problem.detail || reason.problem.title || reason.message
  }
  return reason instanceof Error ? reason.message : 'Falha ao reconciliar a fila.'
}
async function refresh() {
  try {
    error.value = ''
    if (!auth.user) await api.me()
    stations.value = await api.stations()
    if (!selectedStation.value && stations.value.length) selectedStation.value = stations.value[0].id
    items.value = await api.productionItems(selectedStation.value || undefined)
    selected.value = selected.value ? items.value.find(x => x.productionItemId === selected.value?.productionItemId) ?? null : null
  } catch (reason) { error.value = problemMessage(reason) }
}
async function run(item: KitchenDeliveryItem, action: string, operation: () => Promise<unknown>) {
  if (isBusy(item.productionItemId)) return
  setBusy(item.productionItemId, true)
  try { await operation(); intents.clear(`${action}:${item.productionItemId}`); await refresh() }
  catch (reason) { error.value = problemMessage(reason); await refresh() }
  finally { setBusy(item.productionItemId, false) }
}
function send(item: KitchenDeliveryItem) { return run(item, 'send-to-table', () => api.sendToTable(item, intents.for(`send-to-table:${item.productionItemId}`))) }
function confirm(item: KitchenDeliveryItem) { return run(item, 'confirm', () => api.confirmEmployee(item, intents.for(`confirm:${item.deliveryConfirmationId}`))) }
function resolve(item: KitchenDeliveryItem, resolution: 'confirm_delivered' | 'retry_delivery') { return run(item, resolution, () => api.resolve(item, resolution, intents.for(`${resolution}:${item.deliveryContestId}`))) }
function connect() { auth.token = token.value || null; void refresh() }
onMounted(async () => { await refresh(); poll = setInterval(() => void refresh(), 5000); realtime = new OperationsRealtime(() => auth.token, () => { if (!refreshPending) { refreshPending = true; queueMicrotask(() => { refreshPending = false; void refresh() }) } }); try { await realtime.start() } catch { /* polling remains the fallback */ } })
onBeforeUnmount(() => { if (poll) clearInterval(poll); void realtime?.stop() })
</script>

<template>
  <v-container>
    <h1>Fila da cozinha</h1>
    <p>O realtime invalida; a API continua sendo a fonte de verdade.</p>
    <v-text-field v-model="token" label="Access token" type="password" />
    <v-btn @click="connect">Conectar e atualizar</v-btn>
    <v-select v-model="selectedStation" :items="stations" item-title="name" item-value="id" label="Estação" @update:model-value="refresh" />
    <v-alert v-if="error" type="warning" class="mb-3">{{ error }}</v-alert>
    <v-list>
      <v-list-item v-for="item in ordered" :key="item.productionItemId" @click="selected = item">
        <v-list-item-title>#{{ item.queuePosition }} · {{ label(item.status, item.attentionRequired) }}</v-list-item-title>
        <v-list-item-subtitle>{{ item.requiresProduction ? 'Produção física' : 'Aceite operacional' }}<span v-if="item.deliveryContestReason"> · {{ item.deliveryContestReason }}</span></v-list-item-subtitle>
        <template #append>
          <v-progress-circular v-if="isBusy(item.productionItemId)" indeterminate size="22" />
          <v-btn v-else-if="item.status === 'ready' && has('kitchen.delivery.send')" @click.stop="send(item)">Enviar à mesa</v-btn>
          <v-btn v-else-if="item.status === 'awaiting_delivery_confirmation' && item.deliveryConfirmationId && has('kitchen.delivery.confirm')" @click.stop="confirm(item)">Confirmar entrega</v-btn>
          <template v-else-if="item.attentionRequired && has('kitchen.delivery.resolve')">
            <v-btn class="mr-2" @click.stop="resolve(item, 'confirm_delivered')">Confirmar como entregue</v-btn>
            <v-btn variant="outlined" @click.stop="resolve(item, 'retry_delivery')">Refazer entrega</v-btn>
          </template>
        </template>
      </v-list-item>
    </v-list>
    <v-card v-if="selected" title="Detalhe da entrega">
      <v-card-text>Item {{ selected.orderItemId }} · {{ label(selected.status, selected.attentionRequired) }}<br>{{ selected.deliveryContestReason ?? '' }}</v-card-text>
    </v-card>
  </v-container>
</template>
