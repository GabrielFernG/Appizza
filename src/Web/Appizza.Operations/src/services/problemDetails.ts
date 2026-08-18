export interface ProblemDetails {
  status?: number
  title?: string
  detail?: string
  errorCode?: string
  correlationId?: string
}

export class ApiProblem extends Error {
  constructor(public readonly problem: ProblemDetails, status: number) {
    super(problem.detail ?? problem.title ?? `HTTP ${status}`)
    this.name = 'ApiProblem'
  }
}

export async function parseProblem(response: Response): Promise<ApiProblem> {
  let problem: ProblemDetails = {}
  try { problem = await response.json() as ProblemDetails } catch { /* non-JSON response */ }
  return new ApiProblem(problem, response.status)
}
