const rawBaseUrl = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '';
const normalizedBaseUrl = rawBaseUrl.trim().replace(/\/$/, '');

export function getApiBaseUrl() {
  return normalizedBaseUrl;
}

export function buildApiUrl(path: string) {
  return normalizedBaseUrl ? `${normalizedBaseUrl}${path}` : path;
}

export class ApiError extends Error {
  readonly status: number;
  readonly details?: string;

  constructor(status: number, message: string, details?: string) {
    super(message);
    this.status = status;
    this.details = details;
  }
}

export async function apiRequest<T>(path: string, options: RequestInit = {}): Promise<T> {
  const url = buildApiUrl(path);
  const headers = new Headers(options.headers);
  const isFormData =
    typeof FormData !== 'undefined' && options.body instanceof FormData;

  if (!headers.has('Content-Type') && options.body && !isFormData) {
    headers.set('Content-Type', 'application/json');
  }

  const response = await fetch(url, { ...options, headers });
  if (!response.ok) {
    const text = await response.text();
    const message = text || response.statusText || 'Request failed';
    throw new ApiError(response.status, message, text);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const contentType = response.headers.get('content-type') ?? '';
  if (contentType.includes('application/json')) {
    return (await response.json()) as T;
  }

  return (await response.text()) as T;
}

export async function apiHealth(path: string): Promise<boolean> {
  const url = `${normalizedBaseUrl}${path}`;
  const response = await fetch(url, { method: 'GET' });
  return response.ok;
}
