import {
  ActionIcon,
  Badge,
  Center,
  Divider,
  Grid,
  Group,
  Image,
  Loader,
  Paper,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Text,
  ThemeIcon
} from '@mantine/core';
import * as signalR from '@microsoft/signalr';
import {
  IconActivity,
  IconCamera,
  IconClock,
  IconRefresh,
  IconSparkles,
  IconUser
} from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { listCameras, listPersons, searchFaceEvents } from '../api/ingestor';
import { buildSubscriberUrl, fetchFaceSnapshot, getSubscriberBaseUrl } from '../api/subscriber';
import type { CameraResponse, FaceEvent, FaceEventRecord, FaceEventSnapshot } from '../api/types';
import { useI18n } from '../i18n/I18nProvider';
import { formatDateTime } from '../utils/format';

type ConnectionState = 'connecting' | 'live' | 'reconnecting' | 'disconnected' | 'failed';

type WelcomeArrival = {
  id: string;
  personKey: string;
  personName: string;
  personCode?: string | null;
  personId?: string | null;
  cameraId: string;
  cameraLabel: string;
  eventTimeUtc: string;
  imageSrc?: string | null;
  gender?: string | null;
  age?: number | null;
  category?: string | null;
  remarks?: string | null;
  similarity?: number | null;
};

type WelcomeHistory = {
  recent: WelcomeArrival[];
  todayKnownCount: number;
  todayUniqueCount: number;
  uniqueKeys: string[];
};

const MAX_RECENT_ITEMS = 10;
const COUNT_PAGE_SIZE = 200;
const COUNT_PAGE_LIMIT = 10;

const connectionMeta: Record<ConnectionState, { labelKey: string; color: string }> = {
  connecting: { labelKey: 'pages.welcome.status.connecting', color: 'yellow' },
  live: { labelKey: 'pages.welcome.status.live', color: 'brand' },
  reconnecting: { labelKey: 'pages.welcome.status.reconnecting', color: 'orange' },
  disconnected: { labelKey: 'pages.welcome.status.disconnected', color: 'gray' },
  failed: { labelKey: 'pages.welcome.status.failed', color: 'red' }
};

const formatValue = (value?: string | number | null) => {
  if (value === null || value === undefined || value === '') {
    return '-';
  }
  return `${value}`;
};

const resolveImageSrc = (value?: string | null) => {
  if (!value) {
    return null;
  }
  return value.startsWith('data:image') ? value : `data:image/jpeg;base64,${value}`;
};

const getLocalDayKey = (date = new Date()) => {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
};

const getLocalDayRange = () => {
  const now = new Date();
  const from = new Date(now);
  from.setHours(0, 0, 0, 0);
  return {
    dayKey: getLocalDayKey(now),
    fromUtc: from.toISOString(),
    toUtc: now.toISOString()
  };
};

const isSameLocalDay = (value: string, dayKey: string) => {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return false;
  }
  return getLocalDayKey(date) === dayKey;
};

const resolvePersonName = (
  person?: { firstName?: string | null; lastName?: string | null; code?: string | null } | null,
  fallback?: string | null
) => {
  const parts = [person?.firstName, person?.lastName].filter(Boolean);
  const fullName = parts.join(' ').trim();
  if (fullName) {
    return fullName;
  }
  if (person?.code) {
    return person.code;
  }
  return fallback || '-';
};

const resolvePersonKey = (
  personId?: string | null,
  watchlistEntryId?: string | null,
  personCode?: string | null,
  fallbackId?: string
) => {
  return personId || watchlistEntryId || personCode || fallbackId || 'unknown-person';
};

const toSimilarityNumber = (value?: string | number | null) => {
  if (typeof value === 'number') {
    return value;
  }
  if (typeof value === 'string') {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
};

const normalizeGender = (value?: string | null) => {
  if (!value) {
    return null;
  }

  const normalized = value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .trim()
    .toLowerCase();

  if (normalized === 'male' || normalized === 'nam') {
    return 'male';
  }

  if (normalized === 'female' || normalized === 'nu') {
    return 'female';
  }

  return value;
};

const sortByEventTimeDesc = (items: WelcomeArrival[]) =>
  [...items].sort(
    (left, right) => new Date(right.eventTimeUtc).getTime() - new Date(left.eventTimeUtc).getTime()
  );

const mergeRecentArrivals = (incoming: WelcomeArrival[], existing: WelcomeArrival[]) => {
  const merged = new Map<string, WelcomeArrival>();
  [...incoming, ...existing].forEach((item) => {
    if (!merged.has(item.id)) {
      merged.set(item.id, item);
    }
  });
  return sortByEventTimeDesc(Array.from(merged.values())).slice(0, MAX_RECENT_ITEMS);
};

export function Welcome() {
  const { t } = useI18n();
  const [selectedCameraId, setSelectedCameraId] = useState<string | null>(null);
  const [recentArrivals, setRecentArrivals] = useState<WelcomeArrival[]>([]);
  const [featuredArrival, setFeaturedArrival] = useState<WelcomeArrival | null>(null);
  const [todayKnownCount, setTodayKnownCount] = useState(0);
  const [todayUniqueCount, setTodayUniqueCount] = useState(0);
  const [connectionState, setConnectionState] = useState<ConnectionState>('connecting');
  const [lastError, setLastError] = useState<string | null>(null);
  const [featuredAnimationKey, setFeaturedAnimationKey] = useState(0);
  const [highlightedArrivalId, setHighlightedArrivalId] = useState<string | null>(null);
  const seenEventIdsRef = useRef<Set<string>>(new Set());
  const uniquePersonsRef = useRef<Set<string>>(new Set());

  const subscriberTarget = useMemo(() => getSubscriberBaseUrl() || 'same-origin', []);
  const hubUrl = useMemo(() => buildSubscriberUrl('/hubs/faces'), []);
  const dayRange = getLocalDayRange();

  const camerasQuery = useQuery({
    queryKey: ['cameras', 'welcome'],
    queryFn: listCameras
  });

  const cameraOptions = useMemo(() => {
    return (camerasQuery.data ?? [])
      .filter((camera: CameraResponse) => camera.enabled)
      .map((camera: CameraResponse) => ({
        value: camera.cameraId,
        label: `${camera.code?.trim() || camera.cameraId}${camera.ipAddress ? ` - ${camera.ipAddress}` : ''}`
      }));
  }, [camerasQuery.data]);

  const cameraLabelById = useMemo(() => {
    return new Map(
      (camerasQuery.data ?? []).map((camera) => [
        camera.cameraId,
        `${camera.code?.trim() || camera.cameraId}${camera.ipAddress ? ` - ${camera.ipAddress}` : ''}`
      ])
    );
  }, [camerasQuery.data]);

  const personsQuery = useQuery({
    queryKey: ['persons', 'welcome'],
    queryFn: listPersons
  });

  const personById = useMemo(
    () =>
      new Map(
        (personsQuery.data ?? []).map((person) => [
          person.id,
          {
            personalId: person.personalId || person.code || null,
            category: person.category || null,
            gender: person.gender || null,
            age: person.age ?? null,
            remarks: person.remarks || null
          }
        ])
      ),
    [personsQuery.data]
  );

  const buildGreeting = useCallback(
    (_personName: string, eventTimeUtc: string) => {
      const date = new Date(eventTimeUtc);
      const hour = Number.isNaN(date.getTime()) ? new Date().getHours() : date.getHours();
      if (hour < 11) {
        return t('pages.welcome.greetings.morning');
      }
      if (hour < 18) {
        return t('pages.welcome.greetings.afternoon');
      }
      return t('pages.welcome.greetings.evening');
    },
    [t]
  );

  const localizeGender = useCallback(
    (value?: string | null) => {
      const normalized = normalizeGender(value);
      if (normalized === 'male') {
        return t('pages.welcome.gender.male');
      }
      if (normalized === 'female') {
        return t('pages.welcome.gender.female');
      }
      return value ?? '-';
    },
    [t]
  );

  const mapRealtimeEvent = useCallback(
    (event: FaceEvent): WelcomeArrival => {
      const personName = resolvePersonName(event.person, event.personId);
      const personCode = event.person?.code || null;
      return {
        id: event.id,
        personKey: resolvePersonKey(event.personId, event.watchlistEntryId, personCode, event.id),
        personName,
        personCode,
        personId: event.personId,
        cameraId: event.cameraId,
        cameraLabel: cameraLabelById.get(event.cameraId) ?? event.cameraName ?? event.cameraId,
        eventTimeUtc: event.eventTimeUtc,
        imageSrc: resolveImageSrc(event.faceImageBase64),
        gender: event.gender,
        age: event.age,
        category: event.person?.category,
        remarks: event.person?.remarks,
        similarity: toSimilarityNumber(event.similarityText)
      };
    },
    [buildGreeting, cameraLabelById]
  );

  const mapSearchRecord = useCallback(
    (event: FaceEventRecord): WelcomeArrival => {
      const personName = resolvePersonName(event.person, event.personId);
      const personCode = event.person?.code || null;
      return {
        id: event.id,
        personKey: resolvePersonKey(event.personId, event.watchlistEntryId, personCode, event.id),
        personName,
        personCode,
        cameraId: event.cameraId,
        cameraLabel: cameraLabelById.get(event.cameraId) ?? event.cameraId,
        eventTimeUtc: event.eventTimeUtc,
        imageSrc: resolveImageSrc(event.bestshotBase64),
        gender: event.gender,
        age: event.age,
        category: event.person?.category,
        remarks: event.person?.remarks,
        similarity: event.similarity ?? null
      };
    },
    [buildGreeting, cameraLabelById]
  );

  const loadWelcomeHistory = useCallback(
    async (cameraId: string): Promise<WelcomeHistory> => {
      const { fromUtc, toUtc } = getLocalDayRange();
      const [recentResponse, countSeed] = await Promise.all([
        searchFaceEvents({
          fromUtc,
          toUtc,
          cameraIds: [cameraId],
          isKnown: true,
          includeBestshot: true,
          page: 1,
          pageSize: MAX_RECENT_ITEMS
        }),
        searchFaceEvents({
          fromUtc,
          toUtc,
          cameraIds: [cameraId],
          isKnown: true,
          includeBestshot: false,
          page: 1,
          pageSize: COUNT_PAGE_SIZE
        })
      ]);

      const uniqueKeys = new Set<string>();
      countSeed.items.forEach((item) => {
        uniqueKeys.add(
          resolvePersonKey(item.personId, item.watchlistEntryId, item.person?.code, item.id)
        );
      });

      const totalPages = Math.min(
        COUNT_PAGE_LIMIT,
        Math.ceil((countSeed.total || 0) / COUNT_PAGE_SIZE)
      );

      for (let page = 2; page <= totalPages; page += 1) {
        const response = await searchFaceEvents({
          fromUtc,
          toUtc,
          cameraIds: [cameraId],
          isKnown: true,
          includeBestshot: false,
          page,
          pageSize: COUNT_PAGE_SIZE
        });

        response.items.forEach((item) => {
          uniqueKeys.add(
            resolvePersonKey(item.personId, item.watchlistEntryId, item.person?.code, item.id)
          );
        });
      }

      return {
        recent: recentResponse.items.map(mapSearchRecord),
        todayKnownCount: countSeed.total,
        todayUniqueCount: uniqueKeys.size,
        uniqueKeys: Array.from(uniqueKeys)
      };
    },
    [mapSearchRecord]
  );

  useEffect(() => {
    if (cameraOptions.length === 0) {
      setSelectedCameraId(null);
      return;
    }

    if (!selectedCameraId || !cameraOptions.some((option) => option.value === selectedCameraId)) {
      setSelectedCameraId(cameraOptions[0].value);
    }
  }, [cameraOptions, selectedCameraId]);

  useEffect(() => {
    seenEventIdsRef.current = new Set();
    uniquePersonsRef.current = new Set();
    setRecentArrivals([]);
    setFeaturedArrival(null);
    setTodayKnownCount(0);
    setTodayUniqueCount(0);
    setHighlightedArrivalId(null);
  }, [selectedCameraId]);

  const welcomeQuery = useQuery({
    queryKey: ['welcome', 'history', selectedCameraId, dayRange.dayKey],
    queryFn: () => loadWelcomeHistory(selectedCameraId as string),
    enabled: Boolean(selectedCameraId),
    refetchInterval: 30000
  });

  useEffect(() => {
    if (!welcomeQuery.data) {
      return;
    }

    uniquePersonsRef.current = new Set([
      ...Array.from(uniquePersonsRef.current),
      ...welcomeQuery.data.uniqueKeys
    ]);
    welcomeQuery.data.recent.forEach((item) => {
      seenEventIdsRef.current.add(item.id);
    });

    setRecentArrivals((prev) => mergeRecentArrivals(welcomeQuery.data?.recent ?? [], prev));
    setTodayKnownCount((prev) => Math.max(prev, welcomeQuery.data?.todayKnownCount ?? 0));
    setTodayUniqueCount((prev) => Math.max(prev, welcomeQuery.data?.todayUniqueCount ?? 0));

    if (welcomeQuery.data.recent[0]) {
      setFeaturedArrival((prev) => {
        if (!prev || prev.id !== welcomeQuery.data?.recent[0]?.id) {
          setFeaturedAnimationKey((value) => value + 1);
          return welcomeQuery.data?.recent[0] ?? null;
        }
        return prev;
      });
    }
  }, [welcomeQuery.data]);

  const applyRealtimeArrival = useCallback(
    (arrival: WelcomeArrival) => {
      if (!isSameLocalDay(arrival.eventTimeUtc, dayRange.dayKey)) {
        return;
      }

      if (seenEventIdsRef.current.has(arrival.id)) {
        return;
      }

      seenEventIdsRef.current.add(arrival.id);
      setRecentArrivals((prev) => mergeRecentArrivals([arrival], prev));
      setFeaturedArrival(arrival);
      setFeaturedAnimationKey((value) => value + 1);
      setHighlightedArrivalId(arrival.id);
      setTodayKnownCount((prev) => prev + 1);

      if (!uniquePersonsRef.current.has(arrival.personKey)) {
        uniquePersonsRef.current.add(arrival.personKey);
        setTodayUniqueCount((prev) => prev + 1);
      }
    },
    [dayRange.dayKey]
  );

  useEffect(() => {
    if (!highlightedArrivalId) {
      return;
    }

    const timer = window.setTimeout(() => setHighlightedArrivalId(null), 3200);
    return () => window.clearTimeout(timer);
  }, [highlightedArrivalId]);

  useEffect(() => {
    let active = true;
    let retryTimer: number | undefined;
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect([1000, 3000, 5000, 10000])
      .build();

    connection.on('snapshot', (snapshot: FaceEventSnapshot) => {
      if (!active || !selectedCameraId) {
        return;
      }

      const arrivals = (snapshot.known ?? [])
        .filter((event) => event.cameraId === selectedCameraId)
        .map(mapRealtimeEvent);

      if (arrivals.length === 0) {
        return;
      }

      arrivals.forEach((item) => {
        seenEventIdsRef.current.add(item.id);
      });

      setRecentArrivals((prev) => mergeRecentArrivals(arrivals, prev));
      setFeaturedArrival((prev) => prev ?? arrivals[0]);
    });

    connection.on('faceEvent', (event: FaceEvent) => {
      if (!active || !selectedCameraId || !event.isKnown || event.cameraId !== selectedCameraId) {
        return;
      }

      applyRealtimeArrival(mapRealtimeEvent(event));
    });

    connection.onreconnecting(() => {
      if (!active) {
        return;
      }
      setConnectionState('reconnecting');
    });

    connection.onreconnected(() => {
      if (!active) {
        return;
      }
      setConnectionState('live');
    });

    connection.onclose(() => {
      if (!active) {
        return;
      }
      setConnectionState('disconnected');
    });

    const startConnection = async () => {
      setConnectionState('connecting');
      try {
        await connection.start();
        if (!active) {
          return;
        }
        setConnectionState('live');
        setLastError(null);
      } catch (error) {
        if (!active) {
          return;
        }
        setConnectionState('failed');
        setLastError(error instanceof Error ? error.message : t('pages.welcome.errors.connectFailed'));
        retryTimer = window.setTimeout(startConnection, 3000);
      }
    };

    startConnection();

    return () => {
      active = false;
      if (retryTimer) {
        window.clearTimeout(retryTimer);
      }
      void connection.stop();
    };
  }, [applyRealtimeArrival, hubUrl, mapRealtimeEvent, selectedCameraId, t]);

  useEffect(() => {
    if (connectionState === 'live' || !selectedCameraId) {
      return;
    }

    const timer = window.setInterval(async () => {
      try {
        const snapshot = await fetchFaceSnapshot();
        const arrivals = (snapshot.known ?? [])
          .filter((event) => event.cameraId === selectedCameraId)
          .map(mapRealtimeEvent);
        if (arrivals.length > 0) {
          setRecentArrivals((prev) => mergeRecentArrivals(arrivals, prev));
          setFeaturedArrival((prev) => prev ?? arrivals[0]);
        }
      } catch (error) {
        setLastError(
          error instanceof Error ? error.message : t('pages.welcome.errors.loadSnapshot')
        );
      }
    }, 5000);

    return () => window.clearInterval(timer);
  }, [connectionState, mapRealtimeEvent, selectedCameraId, t]);

  const selectedCameraLabel = selectedCameraId
    ? cameraLabelById.get(selectedCameraId) ?? selectedCameraId
    : t('pages.welcome.camera.none');

  const connectionBadge = connectionMeta[connectionState];
  const featuredImage = featuredArrival?.imageSrc ?? null;
  const featuredPersonDetails = featuredArrival?.personId
    ? personById.get(featuredArrival.personId)
    : undefined;

  return (
    <Stack gap="lg" className="page welcome-page">
      <Group justify="space-between" align="flex-start" wrap="wrap">
        <Stack gap={4}>
          <Text size="sm" className="muted-text">
            {t('pages.welcome.subtitle')}
          </Text>
          <Text size="xl" fw={700}>
            {t('pages.welcome.title')}
          </Text>
        </Stack>

        <Group gap="sm" align="center" wrap="wrap">
          <Select
            data={cameraOptions}
            value={selectedCameraId}
            onChange={setSelectedCameraId}
            leftSection={<IconCamera size={16} />}
            placeholder={t('pages.welcome.camera.placeholder')}
            className="welcome-camera-select"
            disabled={cameraOptions.length === 0}
          />
          <ActionIcon
            size="lg"
            variant="light"
            color="brand"
            onClick={() => void welcomeQuery.refetch()}
            aria-label={t('pages.welcome.actions.refresh')}
          >
            <IconRefresh size={18} />
          </ActionIcon>
        </Group>
      </Group>

      <SimpleGrid cols={{ base: 1, md: 3 }} spacing="md">
        <Paper p="md" radius="xl" className="surface-card welcome-stat-card">
          <Group justify="space-between" align="center">
            <Stack gap={2}>
              <Text size="sm" className="muted-text">
                {t('pages.welcome.stats.uniqueToday')}
              </Text>
              <Text size="2rem" fw={700}>
                {todayUniqueCount}
              </Text>
            </Stack>
            <ThemeIcon size={48} radius="xl" variant="light" color="brand">
              <IconUser size={24} />
            </ThemeIcon>
          </Group>
        </Paper>

        <Paper p="md" radius="xl" className="surface-card welcome-stat-card">
          <Group justify="space-between" align="center">
            <Stack gap={2}>
              <Text size="sm" className="muted-text">
                {t('pages.welcome.stats.greetingsToday')}
              </Text>
              <Text size="2rem" fw={700}>
                {todayKnownCount}
              </Text>
            </Stack>
            <ThemeIcon size={48} radius="xl" variant="light" color="brand">
              <IconSparkles size={24} />
            </ThemeIcon>
          </Group>
        </Paper>

        <Paper p="md" radius="xl" className="surface-card welcome-stat-card">
          <Group justify="space-between" align="center">
            <Stack gap={2}>
              <Text size="sm" className="muted-text">
                {t('pages.welcome.stats.liveStatus')}
              </Text>
              <Badge color={connectionBadge.color} variant="light" size="lg">
                {t(connectionBadge.labelKey)}
              </Badge>
            </Stack>
            <ThemeIcon size={48} radius="xl" variant="light" color="brand">
              <IconActivity size={24} />
            </ThemeIcon>
          </Group>
        </Paper>
      </SimpleGrid>

      <Grid gutter="lg" className="welcome-shell">
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <Paper p="xl" radius="2rem" className="surface-card strong welcome-hero">
            <div className="welcome-hero-glow welcome-hero-glow-a" />
            <div className="welcome-hero-glow welcome-hero-glow-b" />

            <Stack gap="lg" style={{ position: 'relative', zIndex: 1 }}>
              <Group justify="space-between" align="flex-start" wrap="wrap">
                <Stack gap={6}>
                  <Text size="sm" className="muted-text">
                    {t('pages.welcome.hero.subtitle')}
                  </Text>
                  <Group gap="sm" wrap="wrap">
                    <Badge variant="light" color="brand" radius="xl" size="lg">
                      {selectedCameraLabel}
                    </Badge>
                    <Badge variant="dot" color={connectionBadge.color} radius="xl" size="lg">
                      {t(connectionBadge.labelKey)}
                    </Badge>
                  </Group>
                </Stack>
                {welcomeQuery.isFetching && (
                  <Group gap="xs" className="muted-text">
                    <Loader size="sm" color="brand" />
                    <Text size="sm">{t('pages.welcome.states.loading')}</Text>
                  </Group>
                )}
              </Group>

              {cameraOptions.length === 0 ? (
                <Center className="welcome-empty-shell">
                  <Stack gap="sm" align="center">
                    <ThemeIcon size={64} radius="xl" variant="light" color="gray">
                      <IconCamera size={28} />
                    </ThemeIcon>
                    <Text fw={600}>{t('pages.welcome.camera.emptyTitle')}</Text>
                    <Text size="sm" className="muted-text" ta="center">
                      {t('pages.welcome.camera.emptyMessage')}
                    </Text>
                  </Stack>
                </Center>
              ) : featuredArrival ? (
                <div key={`${featuredArrival.id}-${featuredAnimationKey}`} className="welcome-hero-panel">
                  <Grid gutter="xl" align="center">
                    <Grid.Col span={{ base: 12, md: 5 }}>
                      <div className="welcome-portrait-shell">
                        <div className="welcome-portrait-ring" />
                        {featuredImage ? (
                          <Image
                            src={featuredImage}
                            alt={featuredArrival.personName}
                            fit="cover"
                            radius={32}
                            className="welcome-portrait"
                          />
                        ) : (
                          <Center className="welcome-portrait welcome-portrait-empty">
                            <IconUser size={68} stroke={1.4} />
                          </Center>
                        )}
                      </div>
                    </Grid.Col>

                    <Grid.Col span={{ base: 12, md: 7 }}>
                      <Stack gap="md">
                        <Group gap="sm" wrap="wrap">
                          <ThemeIcon size={54} radius="xl" variant="light" color="brand">
                            <IconSparkles size={28} />
                          </ThemeIcon>
                          <Text className="welcome-greeting-title">
                            {buildGreeting(featuredArrival.personName, featuredArrival.eventTimeUtc)}
                          </Text>
                        </Group>

                        <Text className="welcome-person-name">{featuredArrival.personName}</Text>

                        <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm">
                          <Paper p="sm" radius="xl" className="welcome-info-pill">
                            <Text size="xs" className="muted-text">
                              {t('common.fields.time')}
                            </Text>
                            <Group gap={8} wrap="nowrap">
                              <IconClock size={14} />
                              <Text fw={600}>{formatDateTime(featuredArrival.eventTimeUtc, t)}</Text>
                            </Group>
                          </Paper>

                          <Paper p="sm" radius="xl" className="welcome-info-pill">
                            <Text size="xs" className="muted-text">
                              {t('common.fields.camera')}
                            </Text>
                            <Group gap={8} wrap="nowrap">
                              <IconCamera size={14} />
                              <Text fw={600}>{featuredArrival.cameraLabel}</Text>
                            </Group>
                          </Paper>
                        </SimpleGrid>

                        <Divider />

                        <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="md">
                          <Stack gap={4}>
                            <Text size="xs" className="muted-text">
                              {t('pages.welcome.fields.personalId')}
                            </Text>
                            <Text fw={600}>
                              {featuredPersonDetails?.personalId ?? featuredArrival.personCode ?? '-'}
                            </Text>
                          </Stack>
                          <Stack gap={4}>
                            <Text size="xs" className="muted-text">
                              {t('pages.welcome.fields.department')}
                            </Text>
                            <Text fw={600}>
                              {formatValue(featuredPersonDetails?.category ?? featuredArrival.category)}
                            </Text>
                          </Stack>
                          <Stack gap={4}>
                            <Text size="xs" className="muted-text">
                              {t('common.fields.gender')}
                            </Text>
                            <Text fw={600}>
                              {localizeGender(featuredPersonDetails?.gender ?? featuredArrival.gender)}
                            </Text>
                          </Stack>
                          <Stack gap={4}>
                            <Text size="xs" className="muted-text">
                              {t('common.fields.age')}
                            </Text>
                            <Text fw={600}>
                              {formatValue(featuredPersonDetails?.age ?? featuredArrival.age)}
                            </Text>
                          </Stack>
                          <Stack gap={4}>
                            <Text size="xs" className="muted-text">
                              {t('common.fields.similarity')}
                            </Text>
                            <Text fw={600}>
                              {featuredArrival.similarity === null || featuredArrival.similarity === undefined
                                ? '-'
                                : featuredArrival.similarity.toFixed(3)}
                            </Text>
                          </Stack>
                          <Stack gap={4}>
                            <Text size="xs" className="muted-text">
                              {t('common.fields.remarks')}
                            </Text>
                            <Text fw={600}>
                              {formatValue(featuredPersonDetails?.remarks ?? featuredArrival.remarks)}
                            </Text>
                          </Stack>
                        </SimpleGrid>
                      </Stack>
                    </Grid.Col>
                  </Grid>
                </div>
              ) : (
                <Center className="welcome-empty-shell">
                  <Stack gap="sm" align="center">
                    <ThemeIcon size={68} radius="xl" variant="light" color="brand">
                      <IconSparkles size={30} />
                    </ThemeIcon>
                    <Text fw={700}>{t('pages.welcome.empty.title')}</Text>
                    <Text size="sm" className="muted-text" ta="center">
                      {t('pages.welcome.empty.message')}
                    </Text>
                  </Stack>
                </Center>
              )}
            </Stack>
          </Paper>
        </Grid.Col>

        <Grid.Col span={{ base: 12, lg: 4 }}>
          <Paper p="lg" radius="2rem" className="surface-card strong welcome-side-panel">
            <Stack gap="md" style={{ height: '100%' }}>
              <Group justify="space-between" align="flex-start">
                <Stack gap={4}>
                  <Text size="sm" className="muted-text">
                    {t('pages.welcome.recent.subtitle')}
                  </Text>
                  <Text size="xl" fw={700}>
                    {t('pages.welcome.recent.title')}
                  </Text>
                </Stack>
                <Badge color="brand" variant="light" radius="xl" size="lg">
                  {recentArrivals.length}
                </Badge>
              </Group>

              <Paper p="md" radius="xl" className="welcome-summary-card">
                <Text size="sm" className="muted-text">
                  {t('pages.welcome.summary.label')}
                </Text>
                <Text className="welcome-summary-value">{todayUniqueCount}</Text>
                <Text size="sm" className="muted-text">
                  {t('pages.welcome.summary.caption', { count: todayKnownCount })}
                </Text>
              </Paper>

              <ScrollArea.Autosize mah={700} offsetScrollbars>
                <Stack gap="sm">
                  {recentArrivals.length === 0 ? (
                    <Center className="welcome-recent-empty">
                      <Stack gap="xs" align="center">
                        <ThemeIcon size={52} radius="xl" variant="light" color="gray">
                          <IconClock size={24} />
                        </ThemeIcon>
                        <Text fw={600}>{t('pages.welcome.recent.emptyTitle')}</Text>
                        <Text size="sm" className="muted-text" ta="center">
                          {t('pages.welcome.recent.emptyMessage')}
                        </Text>
                      </Stack>
                    </Center>
                  ) : (
                    recentArrivals.map((arrival) => (
                      <Paper
                        key={arrival.id}
                        p="md"
                        radius="xl"
                        className={`welcome-recent-card${
                          highlightedArrivalId === arrival.id ? ' is-live' : ''
                        }`}
                      >
                        <Group align="flex-start" wrap="nowrap">
                          <div className="welcome-recent-avatar-shell">
                            {arrival.imageSrc ? (
                              <Image
                                src={arrival.imageSrc}
                                alt={arrival.personName}
                                fit="cover"
                                radius="xl"
                                className="welcome-recent-avatar"
                              />
                            ) : (
                              <Center className="welcome-recent-avatar welcome-recent-avatar-empty">
                                <IconUser size={22} />
                              </Center>
                            )}
                          </div>

                          <Stack gap={4} style={{ flex: 1, minWidth: 0 }}>
                            <Group justify="space-between" align="flex-start" wrap="nowrap">
                              <Stack gap={2} style={{ flex: 1, minWidth: 0 }}>
                                <Text fw={700} lineClamp={1}>
                                  {arrival.personName}
                                </Text>
                                <Text size="sm" className="muted-text" lineClamp={1}>
                                  {arrival.personCode || arrival.cameraLabel}
                                </Text>
                              </Stack>
                              <Badge variant="light" color="brand">
                                {t('common.status.known')}
                              </Badge>
                            </Group>

                            <Text size="sm" className="welcome-recent-greeting" lineClamp={1}>
                              {buildGreeting(arrival.personName, arrival.eventTimeUtc)}
                            </Text>

                            <Group gap="xs" wrap="wrap">
                              <Badge variant="dot" color="gray">
                                {formatDateTime(arrival.eventTimeUtc, t)}
                              </Badge>
                              <Badge variant="dot" color="brand">
                                {arrival.cameraLabel}
                              </Badge>
                            </Group>
                          </Stack>
                        </Group>
                      </Paper>
                    ))
                  )}
                </Stack>
              </ScrollArea.Autosize>

              {lastError && (
                <Text size="sm" c="red">
                  {lastError}
                </Text>
              )}
              <Text size="xs" className="muted-text">
                {t('pages.welcome.subscriberTarget', { target: subscriberTarget })}
              </Text>
            </Stack>
          </Paper>
        </Grid.Col>
      </Grid>
    </Stack>
  );
}
