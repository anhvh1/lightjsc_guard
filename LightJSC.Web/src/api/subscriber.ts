import type { FaceEventSnapshot } from './types';

const rawSubscriberBaseUrl =
  (import.meta.env.VITE_SUBSCRIBER_BASE_URL as string | undefined) ||
  (import.meta.env.VITE_API_BASE_URL as string | undefined) ||
  '';

const normalizedSubscriberBaseUrl = rawSubscriberBaseUrl.trim().replace(/\/$/, '');

export function getSubscriberBaseUrl() {
  return normalizedSubscriberBaseUrl;
}

export function buildSubscriberUrl(path: string) {
  return normalizedSubscriberBaseUrl ? `${normalizedSubscriberBaseUrl}${path}` : path;
}

export async function fetchFaceSnapshot(): Promise<FaceEventSnapshot> {
  const response = await fetch(buildSubscriberUrl('/api/v1/events'));
  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || response.statusText || 'Failed to load face snapshot');
  }

  return (await response.json()) as FaceEventSnapshot;
}
