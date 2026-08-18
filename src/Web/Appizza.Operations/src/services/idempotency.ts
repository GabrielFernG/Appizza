export function newIdempotencyKey(): string {
  return crypto.randomUUID()
}

export class IntentKey {
  private readonly keys = new Map<string, string>()
  for(intent: string): string {
    const current = this.keys.get(intent)
    if (current) return current
    const key = newIdempotencyKey()
    this.keys.set(intent, key)
    return key
  }
  clear(intent: string): void { this.keys.delete(intent) }
}
