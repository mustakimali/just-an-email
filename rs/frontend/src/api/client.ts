const BASE = ''

export async function post<T = unknown>(url: string, body?: unknown): Promise<T> {
  const res = await fetch(BASE + url, {
    method: 'POST',
    headers: body ? { 'Content-Type': 'application/json' } : {},
    body: body ? JSON.stringify(body) : undefined,
  })
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  const text = await res.text()
  if (!text) return undefined as T
  return JSON.parse(text)
}

export async function get<T = unknown>(url: string): Promise<T> {
  const res = await fetch(BASE + url)
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  return res.json()
}

export async function postForm(url: string, formData: FormData): Promise<Response> {
  return fetch(BASE + url, {
    method: 'POST',
    body: formData,
  })
}
