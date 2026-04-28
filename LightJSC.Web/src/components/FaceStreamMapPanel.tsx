import { Badge, Box, Center, Group, Image, Paper, Stack, Text } from '@mantine/core';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiError } from '../api/client';
import { traceFaceByEvent, traceFaceByPerson } from '../api/ingestor';
import type { FaceEvent, FaceEventRecord } from '../api/types';
import { useI18n } from '../i18n/I18nProvider';
import { formatDateTime } from '../utils/format';
import { FaceTraceMapPanel } from './FaceTraceMapPanel';

const TRACE_WINDOW_MS = 5 * 60 * 1000;
const REFRESH_INTERVAL_MS = 3000;
const TRACE_TOP_K = 50;
const TRACE_SIMILARITY_MIN = 0.7;
const STREAM_ACCENT = '#ff3030';
const GUID_EMPTY = '00000000-0000-0000-0000-000000000000';
const GUID_REGEX = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

const normalizeBase64 = (value?: string | null) => {
  if (!value) {
    return null;
  }
  const marker = 'base64,';
  const index = value.indexOf(marker);
  return index >= 0 ? value.slice(index + marker.length) : value;
};

const formatCameraLabel = (event: FaceEvent) => {
  const code = event.cameraCode?.trim();
  const ip = event.cameraIp?.trim();
  if (code && ip) {
    return `${code} - ${ip}`;
  }
  return code || ip || event.cameraName || event.cameraId;
};

const formatPersonName = (event: FaceEvent) => {
  const person = event.person;
  const fallback = event.personId || '-';
  if (!person) {
    return fallback;
  }

  const parts = [];
  if (person.firstName) parts.push(person.firstName);
  if (person.lastName) parts.push(person.lastName);
  const name = parts.join(' ').trim();
  if (name) {
    return person.code ? `${name} (${person.code})` : name;
  }

  return person.code || fallback;
};

const isGuid = (value?: string | null) => {
  if (!value) {
    return false;
  }
  const trimmed = value.trim();
  if (!GUID_REGEX.test(trimmed)) {
    return false;
  }
  return trimmed !== GUID_EMPTY;
};

const toTraceRecord = (event: FaceEvent): FaceEventRecord => ({
  id: event.id,
  eventTimeUtc: event.eventTimeUtc,
  cameraId: event.cameraId,
  cameraIp: event.cameraIp ?? null,
  cameraZone: event.zone ?? null,
  isKnown: event.isKnown,
  watchlistEntryId: event.watchlistEntryId ?? null,
  personId: event.personId ?? null,
  person: event.person ?? null,
  similarity: null,
  score: null,
  bestshotBase64: normalizeBase64(event.faceImageBase64),
  gender: event.gender ?? null,
  age: event.age ?? null,
  mask: event.mask ?? null,
  hasFeature: Boolean(event.featureBase64),
  traceSimilarity: null
});

const mergeTraceResults = (items: FaceEventRecord[], seedEvent?: FaceEvent | null) => {
  const merged = new Map<string, FaceEventRecord>();
  items.forEach((item) => {
    merged.set(item.id, {
      ...item,
      bestshotBase64: normalizeBase64(item.bestshotBase64)
    });
  });

  if (seedEvent) {
    const seedRecord = toTraceRecord(seedEvent);
    const existing = merged.get(seedRecord.id);
    if (existing) {
      merged.set(seedRecord.id, {
        ...existing,
        bestshotBase64: existing.bestshotBase64 || seedRecord.bestshotBase64
      });
    } else {
      merged.set(seedRecord.id, seedRecord);
    }
  }

  return Array.from(merged.values());
};

const resolveReferenceTimeMs = (seedEvent?: FaceEvent | null) => {
  if (!seedEvent?.eventTimeUtc) {
    return Date.now();
  }
  const eventMs = new Date(seedEvent.eventTimeUtc).getTime();
  if (Number.isNaN(eventMs)) {
    return Date.now();
  }
  return Math.max(Date.now(), eventMs);
};

const filterRecentEvents = (items: FaceEventRecord[], referenceMs: number) => {
  const cutoff = referenceMs - TRACE_WINDOW_MS;
  return items.filter((item) => {
    const time = new Date(item.eventTimeUtc).getTime();
    return !Number.isNaN(time) && time >= cutoff && time <= referenceMs;
  });
};

const trimRecentEvents = (items: FaceEventRecord[]) => {
  const sorted = [...items].sort(
    (left, right) =>
      new Date(left.eventTimeUtc).getTime() - new Date(right.eventTimeUtc).getTime()
  );
  if (sorted.length <= TRACE_TOP_K) {
    return sorted;
  }
  return sorted.slice(sorted.length - TRACE_TOP_K);
};

export function FaceStreamMapPanel({
  seedEvent,
  isActive = true
}: {
  seedEvent: FaceEvent | null;
  isActive?: boolean;
}) {
  const { t } = useI18n();
  const [traceResults, setTraceResults] = useState<FaceEventRecord[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
  const requestIdRef = useRef(0);
  const inFlightRef = useRef(false);

  const fetchTrace = useCallback(async () => {
    if (!seedEvent || inFlightRef.current) {
      return;
    }

    inFlightRef.current = true;
    const requestId = requestIdRef.current + 1;
    requestIdRef.current = requestId;
    setLoading(true);

    try {
      const referenceMs = resolveReferenceTimeMs(seedEvent);
      const now = new Date(referenceMs);
      const from = new Date(referenceMs - TRACE_WINDOW_MS);
      const filter = {
        fromUtc: from.toISOString(),
        toUtc: now.toISOString()
      };
      const payload = {
        topK: TRACE_TOP_K,
        similarityMin: TRACE_SIMILARITY_MIN,
        includeBestshot: true,
        filter
      };

      const personId = seedEvent.personId?.trim();
      let response: { items?: FaceEventRecord[] };
      if (isGuid(personId)) {
        try {
          response = await traceFaceByPerson({ personId: personId as string, ...payload });
        } catch (err) {
          if (err instanceof ApiError && (err.status === 400 || err.status === 404)) {
            response = await traceFaceByEvent(seedEvent.id, payload);
          } else {
            throw err;
          }
        }
      } else {
        response = await traceFaceByEvent(seedEvent.id, payload);
      }

      if (requestId !== requestIdRef.current) {
        return;
      }

      const merged = mergeTraceResults(response.items ?? [], seedEvent);
      const filtered = filterRecentEvents(merged, referenceMs);
      setTraceResults(trimRecentEvents(filtered));
      setError(null);
      setLastUpdated(new Date());
    } catch (err) {
      if (requestId !== requestIdRef.current) {
        return;
      }
      setError(
        err instanceof Error ? err.message : t('pages.faceStream.map.errors.loadFailed')
      );
      const referenceMs = resolveReferenceTimeMs(seedEvent);
      setTraceResults((prev) => {
        if (prev.length) {
          return prev;
        }
        return trimRecentEvents(
          filterRecentEvents(mergeTraceResults([], seedEvent), referenceMs)
        );
      });
    } finally {
      if (requestId === requestIdRef.current) {
        setLoading(false);
      }
      inFlightRef.current = false;
    }
  }, [seedEvent]);

  useEffect(() => {
    setTraceResults([]);
    setError(null);
    setLastUpdated(null);
    if (!seedEvent || !isActive) {
      return;
    }
    void fetchTrace();
  }, [fetchTrace, isActive, seedEvent]);

  useEffect(() => {
    if (!seedEvent || !isActive) {
      return;
    }
    const timer = window.setInterval(() => {
      void fetchTrace();
    }, REFRESH_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [fetchTrace, isActive, seedEvent]);

  const faceImage = useMemo(() => {
    const base64 = normalizeBase64(seedEvent?.faceImageBase64);
    return base64 ? `data:image/jpeg;base64,${base64}` : null;
  }, [seedEvent]);

  const headerLabel = seedEvent ? formatPersonName(seedEvent) : '-';
  const cameraLabel = seedEvent ? formatCameraLabel(seedEvent) : '-';

  if (!seedEvent) {
    return (
      <Paper p="lg" radius="lg" className="surface-card">
        <Center h={240}>
          <Stack gap={6} align="center">
            <Text size="sm" fw={600}>
              {t('pages.faceStream.map.empty.title')}
            </Text>
            <Text size="xs" className="muted-text" ta="center">
              {t('pages.faceStream.map.empty.subtitle')}
            </Text>
          </Stack>
        </Center>
      </Paper>
    );
  }

  return (
    <Stack gap="lg">
      <Paper p="md" radius="lg" className="surface-card strong">
        <Group justify="space-between" align="center" wrap="nowrap">
          <Group gap="md" wrap="nowrap">
            {faceImage ? (
              <Image src={faceImage} w={64} h={64} radius="md" fit="cover" />
            ) : (
              <Center
                w={64}
                h={64}
                className="surface-card"
                style={{ borderRadius: 12, borderStyle: 'dashed' }}
              >
                <Text size="xs" className="muted-text" ta="center">
                  {t('common.empty.noImage')}
                </Text>
              </Center>
            )}
            <Stack gap={4}>
              <Text size="xs" className="muted-text">
                {t('pages.faceStream.map.header.subtitle')}
              </Text>
              <Text size="lg" fw={600}>
                {headerLabel}
              </Text>
              <Text size="xs" className="muted-text">
                {t('pages.faceStream.map.header.atCamera', { time: formatDateTime(seedEvent.eventTimeUtc, t), camera: cameraLabel })}
              </Text>
            </Stack>
          </Group>
          <Stack gap={4} align="flex-end">
            <Badge color={seedEvent.isKnown ? 'brand' : 'orange'} variant="light">
              {seedEvent.isKnown ? t('common.status.known') : t('common.status.unknown')}
            </Badge>
            <Text size="xs" className="muted-text">
              {loading
                ? t('common.states.updating')
                : lastUpdated
                  ? t('common.states.updatedAt', {
                      time: formatDateTime(lastUpdated.toISOString(), t)
                    })
                  : t('common.states.awaiting')}
            </Text>
          </Stack>
        </Group>
      </Paper>

      <Box style={{ height: '70vh', minHeight: 420 }}>
        <FaceTraceMapPanel
          results={traceResults}
          accentColor={STREAM_ACCENT}
          highlightLatest
        />
      </Box>

      {!loading && traceResults.length === 0 && (
        <Text size="sm" className="muted-text">
          {t('pages.faceStream.map.emptyResults')}
        </Text>
      )}
      {error && (
        <Text size="sm" c="red">
          {error}
        </Text>
      )}
    </Stack>
  );
}

