export type CommunicationStatus = 'draft' | 'published' | 'paused' | 'expired' | 'archived'
export interface Communication { id: string; title: string; body?: string | null; mediaAssetId?: string | null; mediaType: 'image' | 'video'; status: CommunicationStatus; priority: number; startsAt: string; endsAt: string; version: number }
export interface MediaAsset { id: string; fileName: string; mimeType: string; status: string; fileSize: number; createdAt: string; updatedAt: string }
