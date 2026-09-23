import { expect, test, type Page, type TestInfo } from '@playwright/test'
import { readFileSync } from 'node:fs'

type Manifest = { operationsUrl: string; apiUrl: string; auth: { refundToken: string; noRefundToken: string }; scenarios: Record<string, { sessionId: string; paymentAttemptId: string }> }
function manifest(): Manifest {
  const path = process.env.APPIZZA_E2E_MANIFEST
  if (!path) throw new Error('E2E environment not initialized: APPIZZA_E2E_MANIFEST is missing.')
  const value = JSON.parse(readFileSync(path, 'utf8')) as Manifest
  for (const key of ['cash', 'providerSuccess', 'unknown', 'failed', 'multiplePartial', 'permission']) if (!value.scenarios[key]) throw new Error(`E2E environment not initialized: scenario ${key} is missing.`)
  return value
}

async function openPayments(page: Page, name: string, testInfo: TestInfo) {
  const data = manifest(); const scenario = data.scenarios[name]
  const consoleErrors: string[] = []; const pageErrors: string[] = []; const failedRequests: string[] = []; const httpErrors: string[] = []
  page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()) })
  page.on('pageerror', error => pageErrors.push(error.message))
  page.on('requestfailed', request => failedRequests.push(`${request.method()} ${request.url()} :: ${request.failure()?.errorText ?? 'unknown'}`))
  page.on('response', response => { if (response.status() >= 400) httpErrors.push(`${response.status()} ${response.request().method()} ${response.url()}`) })
  await page.addInitScript((value: string) => sessionStorage.setItem('appizza.accessToken', value), data.auth.refundToken)
  await page.goto(`/closing-payments?sessionId=${encodeURIComponent(scenario.sessionId)}`)
  try { await expect(page.getByTestId('payment-detail')).toBeVisible() } catch (error) {
    const body = (await page.locator('body').innerText().catch(() => '')).slice(0, 4000)
    const diagnostic = [`URL: ${page.url()}`, `TITLE: ${await page.title().catch(() => '')}`, `BODY:\n${body}`, `CONSOLE_ERRORS:\n${consoleErrors.join('\n')}`, `PAGE_ERRORS:\n${pageErrors.join('\n')}`, `FAILED_REQUESTS:\n${failedRequests.join('\n')}`, `HTTP_ERRORS:\n${httpErrors.join('\n')}`].join('\n\n')
    await testInfo.attach('closing-payments-diagnostics', { body: diagnostic, contentType: 'text/plain' })
    console.error(diagnostic)
    throw error
  }
}

async function waitForAuthoritativeStatus(page: Page, sessionId: string, token: string, status: string) {
  const apiUrl = manifest().apiUrl
  await expect.poll(async () => (await page.request.get(`${apiUrl}/api/v1/operations/sessions/${sessionId}/payments`, { headers: { Authorization: `Bearer ${token}` } })).text(), { timeout: 30_000 }).toContain(status)
  await page.reload()
}
async function releaseProvider(page: Page, provider: string) {
  const data = manifest()
  const response = await page.request.post(`${data.apiUrl}/api/v1/e2e/provider-control/${provider}/release`)
  expect(response.ok()).toBeTruthy()
}

test.describe('Operations refund real-browser flows', () => {
  test('Cash create, authoritative refetch and explicit confirmation', async ({ page }) => {
    await openPayments(page, 'cash', test.info())
    const payment = page.getByTestId('payment-attempt-Cash').first()
    await expect(payment).toContainText('Disponível')
    await payment.getByLabel('Valor do refund').fill(process.env.APPIZZA_E2E_CASH_AMOUNT ?? '1.00')
    await payment.getByLabel('Motivo').fill('E2E cash refund')
    await payment.getByRole('button', { name: 'Criar refund' }).click()
    await expect(payment).toContainText('Created')
    page.once('dialog', dialog => dialog.accept())
    await payment.getByRole('button', { name: 'Confirmar Cash' }).click()
    await expect(payment).toContainText('Completed')
  })

  test('provider-backed refund never declares local success and later reflects completion', async ({ page }) => {
    await openPayments(page, 'providerSuccess', test.info())
    const payment = page.getByTestId('payment-attempt-Pix').first()
    await payment.getByLabel('Valor do refund').fill(process.env.APPIZZA_E2E_PROVIDER_AMOUNT ?? '1.00')
    await payment.getByLabel('Motivo').fill('E2E provider refund')
    await payment.getByRole('button', { name: 'Criar refund' }).click()
    await expect(payment).toContainText(/Created|Processing/)
    await waitForAuthoritativeStatus(page, manifest().scenarios.providerSuccess.sessionId, manifest().auth.refundToken, 'Completed')
    await expect(payment).toContainText('Completed')
  })

  test('unknown provider outcome is rendered as reconciliation required, then converges', async ({ page }) => {
    await openPayments(page, 'unknown', test.info())
    const payment = page.getByTestId('payment-attempt-Pix').first()
    await expect(payment).toContainText(/Processing|Reconciliação necessária/)
    await releaseProvider(page, 'unknown')
    await waitForAuthoritativeStatus(page, manifest().scenarios.unknown.sessionId, manifest().auth.refundToken, 'Completed')
    await expect(payment).toContainText('Completed')
    await expect(payment).not.toContainText('Reconciliação necessária')
  })

  test('definitive provider failure releases availability from authoritative read', async ({ page }) => {
    await openPayments(page, 'failed', test.info())
    const payment = page.getByTestId('payment-attempt-Debit').first()
    await expect(payment).toContainText('Failed')
    await expect(payment).not.toContainText('Reconciliação necessária')
  })

  test('partial refunds display backend composition and double submit remains one intent', async ({ page }) => {
    await openPayments(page, 'multiplePartial', test.info())
    const payment = page.getByTestId('payment-attempt-Pix').first()
    await expect(payment).toContainText('Reembolsado: 20')
    await expect(payment).toContainText('Em processamento: 15')
    await expect(payment).toContainText('Disponível: 65')
    await payment.getByLabel('Valor do refund').fill('1.00')
    await payment.getByLabel('Motivo').fill('E2E partial refund')
    const button = payment.getByRole('button', { name: 'Criar refund' })
    await expect(button).toBeEnabled()
    await releaseProvider(page, 'multiplePartial')
    const data = manifest(); const key = crypto.randomUUID(); const headers = { Authorization: `Bearer ${data.auth.refundToken}`, 'Content-Type': 'application/json', 'Idempotency-Key': key }
    const requests = await Promise.all([
      page.request.post(`${data.apiUrl}/api/v1/payments/${data.scenarios.multiplePartial.paymentAttemptId}/refunds`, { headers, data: { amount: 1, reason: 'E2E duplicate intent' } }),
      page.request.post(`${data.apiUrl}/api/v1/payments/${data.scenarios.multiplePartial.paymentAttemptId}/refunds`, { headers, data: { amount: 1, reason: 'E2E duplicate intent' } }),
    ])
    expect(requests.every(request => request.ok())).toBeTruthy()
    const details = await page.request.get(`${data.apiUrl}/api/v1/operations/sessions/${data.scenarios.multiplePartial.sessionId}/payments`, { headers: { Authorization: `Bearer ${data.auth.refundToken}` } })
    const body = await details.json(); const attempt = body.paymentAttempts.find((item: { id: string }) => item.id === data.scenarios.multiplePartial.paymentAttemptId)
    expect(attempt.refunds.filter((refund: { reason: string }) => refund.reason === 'E2E duplicate intent')).toHaveLength(1)
  })

  test('employee without payments.refund does not see refund actions', async ({ page }) => {
    const data = manifest(); await page.addInitScript((value: string) => sessionStorage.setItem('appizza.accessToken', value), data.auth.noRefundToken)
    const consoleErrors: string[] = []; const pageErrors: string[] = []; const failedRequests: string[] = []; const httpErrors: string[] = []
    page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()) })
    page.on('pageerror', error => pageErrors.push(error.message))
    page.on('requestfailed', request => failedRequests.push(`${request.method()} ${request.url()} :: ${request.failure()?.errorText ?? 'unknown'}`))
    page.on('response', response => { if (response.status() >= 400) httpErrors.push(`${response.status()} ${response.request().method()} ${response.url()}`) })
    await page.goto(`/closing-payments?sessionId=${encodeURIComponent(data.scenarios.permission.sessionId)}`)
    try { await expect(page.getByTestId('payment-detail')).toBeVisible() } catch (error) { const body = (await page.locator('body').innerText().catch(() => '')).slice(0, 4000); await test.info().attach('closing-payments-diagnostics', { body: [`URL: ${page.url()}`, `BODY:\n${body}`, `CONSOLE_ERRORS:\n${consoleErrors.join('\n')}`, `PAGE_ERRORS:\n${pageErrors.join('\n')}`, `FAILED_REQUESTS:\n${failedRequests.join('\n')}`, `HTTP_ERRORS:\n${httpErrors.join('\n')}`].join('\n\n'), contentType: 'text/plain' }); throw error }
    await expect(page.getByRole('button', { name: 'Criar refund' })).toHaveCount(0)
  })
})
