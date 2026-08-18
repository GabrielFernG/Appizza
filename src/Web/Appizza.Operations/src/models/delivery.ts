export type DeliveryConfirmationStatus = 'pending' | 'contested' | 'confirmed_manual' | 'confirmed_automatic'
export type DeliveryContestStatus = 'open'

export interface KitchenDeliveryItem {
  productionItemId: string
  orderItemId: string
  stationId: string
  status: string
  queuePosition: number
  requiresProduction: boolean
  productionVersion: number
  deliveryConfirmationId: string | null
  deliveryConfirmationStatus: DeliveryConfirmationStatus | null
  deliveryConfirmationVersion: number | null
  deliveryConfirmationSequence: number | null
  deliveryConfirmationRequestedAt: string | null
  deliveryConfirmationConfirmedAt: string | null
  deliveryContestId: string | null
  deliveryContestStatus: DeliveryContestStatus | null
  deliveryContestVersion: number | null
  deliveryContestReason: string | null
  deliveryContestCreatedAt: string | null
  attentionRequired: boolean
}
