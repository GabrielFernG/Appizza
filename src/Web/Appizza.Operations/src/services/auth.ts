export interface CurrentUser {
  id: string
  name: string
  establishmentId: string
  permissions: string[]
}

export class AuthContext {
  private current: CurrentUser | null = null
  constructor(private readonly tokenStore: Storage = sessionStorage) {}
  get token(): string | null { return this.tokenStore.getItem('appizza.accessToken') }
  set token(value: string | null) {
    if (value) this.tokenStore.setItem('appizza.accessToken', value)
    else this.tokenStore.removeItem('appizza.accessToken')
  }
  get user(): CurrentUser | null { return this.current }
  set user(value: CurrentUser | null) { this.current = value }
  has(permission: string): boolean { return this.current?.permissions.includes(permission) ?? false }
}
