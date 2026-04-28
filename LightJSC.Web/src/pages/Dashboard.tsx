import {
  ActionIcon,
  Badge,
  Box,
  Center,
  Divider,
  Grid,
  Group,
  Image,
  Paper,
  Progress,
  ScrollArea,
  SegmentedControl,
  SimpleGrid,
  Stack,
  Switch,
  Text,
  useMantineColorScheme
} from '@mantine/core';
import {
  IconActivity,
  IconAlertTriangle,
  IconBolt,
  IconCamera,
  IconClock,
  IconEyeOff,
  IconRefresh,
  IconShieldCheck,
  IconWifiOff
} from '@tabler/icons-react';
import * as signalR from '@microsoft/signalr';
import { useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import ReactECharts from 'echarts-for-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import {
  getDashboardAlerts,
  getDashboardCameraHealth,
  getDashboardSummary,
  getDashboardSystemMetrics,
  getDashboardTimeseries,
  getDashboardTop,
  getFaceEventBestshot
} from '../api/ingestor';
import { buildSubscriberUrl, fetchFaceSnapshot } from '../api/subscriber';
import type { DashboardAlertItem, DashboardTimeseriesPoint, DashboardTopItem, FaceEvent } from '../api/types';
import { DashboardMapPanel } from '../components/DashboardMapPanel';
import { MetricCard } from '../components/MetricCard';
import { useI18n } from '../i18n/I18nProvider';
import { formatDateTime } from '../utils/format';

const RANGE_PRESETS = [
  { value: '5m', label: '5m', minutes: 5, stepSeconds: 5 },
  { value: '15m', label: '15m', minutes: 15, stepSeconds: 5 },
  { value: '1h', label: '1h', minutes: 60, stepSeconds: 10 },
  { value: '6h', label: '6h', minutes: 360, stepSeconds: 60 },
  { value: '24h', label: '24h', minutes: 1440, stepSeconds: 300 }
];

const formatValue = (value: number | null | undefined, locale: string) => {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '-';
  }
  return value.toLocaleString(locale);
};

const resolveFaceImage = (value?: string | null) => {
  if (!value) {
    return null;
  }
  return value.startsWith('data:image') ? value : `data:image/jpeg;base64,${value}`;
};

const resolveSeverityColor = (severity: string) => {
  switch (severity) {
    case 'CRITICAL':
      return 'red';
    case 'HIGH':
      return 'orange';
    case 'LOW':
      return 'teal';
    default:
      return 'yellow';
  }
};

const buildBuckets = (fromUtc: string, toUtc: string, stepSeconds: number) => {
  const stepMs = stepSeconds * 1000;
  const from = new Date(fromUtc).getTime();
  const to = new Date(toUtc).getTime();
  const start = Math.floor(from / stepMs) * stepMs;
  const end = Math.ceil(to / stepMs) * stepMs;
  const buckets: number[] = [];
  for (let ts = start; ts <= end; ts += stepMs) {
    buckets.push(ts);
  }
  return buckets;
};

const buildCumulativeSeries = (buckets: number[], pointsByBucket: Map<number, DashboardTimeseriesPoint>) => {
  const seriesKnown: Array<[number, number]> = [];
  const seriesUnknown: Array<[number, number]> = [];
  const seriesTotal: Array<[number, number]> = [];

  let lastKnown = 0;
  let lastUnknown = 0;
  let lastTotal = 0;
  for (const bucket of buckets) {
    const point = pointsByBucket.get(bucket);

    if (point) {
      if (typeof point.knownCumulative === 'number') {
        lastKnown = point.knownCumulative;
      } else {
        lastKnown = point.knownCount ?? lastKnown;
      }

      if (typeof point.unknownCumulative === 'number') {
        lastUnknown = point.unknownCumulative;
      } else {
        lastUnknown = point.unknownCount ?? lastUnknown;
      }

      if (typeof point.totalCumulative === 'number') {
        lastTotal = point.totalCumulative;
      } else {
        lastTotal = point.totalCount ?? lastTotal;
      }

    }

    seriesKnown.push([bucket, lastKnown]);
    seriesUnknown.push([bucket, lastUnknown]);
    seriesTotal.push([bucket, lastTotal]);
  }

  return { seriesKnown, seriesUnknown, seriesTotal };
};

const buildTopSeries = (items: DashboardTopItem[]) => {
  return items.map((item) => ({ name: item.label, value: item.count }));
};

export function Dashboard() {
  const { t, language } = useI18n();
  const locale = language === 'vi' ? 'vi-VN' : 'en-US';
  const queryClient = useQueryClient();
  const { colorScheme } = useMantineColorScheme();
  const isDark = colorScheme === 'dark';
  const [rangeKey, setRangeKey] = useState<string>('15m');
  const [autoRefresh, setAutoRefresh] = useState(true);
  const hubUrl = useMemo(() => buildSubscriberUrl('/hubs/faces'), []);
  const faceImageMapRef = useRef<Map<string, string>>(new Map());
  const [faceImageVersion, setFaceImageVersion] = useState(0);
  const range = RANGE_PRESETS.find((item) => item.value === rangeKey) ?? RANGE_PRESETS[0];

  const refetchAll = () => {
    void queryClient.invalidateQueries({ queryKey: ['dashboard'] });
  };

  const handleRefreshNow = () => {
    refetchAll();
  };

  const summaryQuery = useQuery({
    queryKey: ['dashboard', 'summary', rangeKey],
    queryFn: () => {
      const now = new Date();
      const from = new Date(now.getTime() - range.minutes * 60 * 1000);
      return getDashboardSummary({ fromUtc: from.toISOString(), toUtc: now.toISOString() });
    },
    refetchInterval: autoRefresh ? 5000 : false
  });

  const timeseriesQuery = useQuery({
    queryKey: ['dashboard', 'timeseries', rangeKey],
    queryFn: () => {
      const now = new Date();
      const from = new Date(now.getTime() - range.minutes * 60 * 1000);
      return getDashboardTimeseries({
        metric: 'events',
        fromUtc: from.toISOString(),
        toUtc: now.toISOString(),
        stepSeconds: range.stepSeconds
      });
    },
    refetchInterval: autoRefresh ? 5000 : false
  });

  const topQuery = useQuery({
    queryKey: ['dashboard', 'top', rangeKey],
    queryFn: () => {
      const now = new Date();
      const from = new Date(now.getTime() - range.minutes * 60 * 1000);
      return getDashboardTop({
        metric: 'events',
        groupBy: 'camera',
        fromUtc: from.toISOString(),
        toUtc: now.toISOString(),
        limit: 10
      });
    },
    refetchInterval: autoRefresh ? 10000 : false
  });

  const alertsQuery = useQuery({
    queryKey: ['dashboard', 'alerts', rangeKey],
    queryFn: () => {
      const now = new Date();
      const from = new Date(now.getTime() - range.minutes * 60 * 1000);
      return getDashboardAlerts({ fromUtc: from.toISOString(), toUtc: now.toISOString(), limit: 5 });
    },
    refetchInterval: autoRefresh ? 5000 : false
  });

  const faceSnapshotQuery = useQuery({
    queryKey: ['dashboard', 'face-snapshot'],
    queryFn: fetchFaceSnapshot,
    refetchInterval: autoRefresh ? 5000 : false
  });

  useEffect(() => {
    const snapshot = faceSnapshotQuery.data;
    if (!snapshot) {
      return;
    }
    const next = new Map<string, string>();
    [...(snapshot.known ?? []), ...(snapshot.unknown ?? [])].forEach((event) => {
      if (event.faceImageBase64) {
        next.set(event.id, event.faceImageBase64);
      }
    });
    faceImageMapRef.current = next;
    setFaceImageVersion((prev) => prev + 1);
  }, [faceSnapshotQuery.data]);

  useEffect(() => {
    let active = true;
    let retryTimer: number | undefined;
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect([1000, 3000, 5000, 10000])
      .build();

    connection.on('faceEvent', (event: FaceEvent) => {
      if (!active) {
        return;
      }
      if (!event.faceImageBase64) {
        return;
      }
      const next = new Map(faceImageMapRef.current);
      next.set(event.id, event.faceImageBase64);
      faceImageMapRef.current = next;
      setFaceImageVersion((prev) => prev + 1);
    });

    const startConnection = async () => {
      try {
        await connection.start();
      } catch (error) {
        if (!active) {
          return;
        }
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
  }, [hubUrl]);

  const cameraHealthQuery = useQuery({
    queryKey: ['dashboard', 'camera-health'],
    queryFn: getDashboardCameraHealth,
    refetchInterval: autoRefresh ? 10000 : false
  });

  const systemQuery = useQuery({
    queryKey: ['dashboard', 'system-metrics'],
    queryFn: getDashboardSystemMetrics,
    refetchInterval: autoRefresh ? 10000 : false
  });

  const summary = summaryQuery.data;
  const timeseries = timeseriesQuery.data;
  const buckets = useMemo(
    () => (timeseries ? buildBuckets(timeseries.fromUtc, timeseries.toUtc, timeseries.stepSeconds) : []),
    [timeseries]
  );

  const pointsByBucket = useMemo(() => {
    const map = new Map<number, DashboardTimeseriesPoint>();
    if (!timeseries) {
      return map;
    }
    timeseries.points.forEach((point) => {
      map.set(new Date(point.bucketStartUtc).getTime(), point);
    });
    return map;
  }, [timeseries]);

  const { seriesKnown, seriesUnknown, seriesTotal } = useMemo(
    () => buildCumulativeSeries(buckets, pointsByBucket),
    [buckets, pointsByBucket]
  );

  const throughputOption = useMemo(
    () => {
      const axisColor = isDark ? 'rgba(255, 255, 255, 0.85)' : '#111111';
      const tickColor = isDark ? 'rgba(255, 255, 255, 0.55)' : '#111111';
      const splitColor = isDark ? 'rgba(255, 255, 255, 0.18)' : 'rgba(17, 17, 17, 0.18)';

      return {
        tooltip: { trigger: 'axis', axisPointer: { type: 'line' } },
        legend: {
          right: 0,
          top: 'middle',
          orient: 'vertical',
          textStyle: { color: axisColor }
        },
        grid: { left: 56, right: 140, top: 24, bottom: 56 },
        xAxis: {
          type: 'time',
          boundaryGap: false,
          name: t('pages.dashboard.throughput.axis.time'),
          nameLocation: 'middle',
          nameGap: 28,
          nameTextStyle: { color: axisColor, fontWeight: 600 },
          axisLine: { lineStyle: { color: axisColor } },
          axisTick: { show: true, lineStyle: { color: tickColor } },
          axisLabel: { color: axisColor },
          splitLine: {
            show: true,
            lineStyle: { color: splitColor, type: 'dashed' }
          }
        },
        yAxis: {
          type: 'value',
          name: t('pages.dashboard.throughput.axis.events'),
          nameLocation: 'middle',
          nameGap: 36,
          nameTextStyle: { color: axisColor, fontWeight: 600 },
          axisLine: { lineStyle: { color: axisColor } },
          axisTick: { show: true, lineStyle: { color: tickColor } },
          axisLabel: { color: axisColor },
          splitLine: {
            show: true,
            lineStyle: { color: splitColor, type: 'dashed' }
          }
        },
      series: [
        {
          name: t('pages.dashboard.throughput.legend.known'),
          type: 'line',
          smooth: false,
          showSymbol: false,
          symbol: 'none',
          data: seriesKnown,
          lineStyle: { color: '#3b82f6', width: 1.2 },
          itemStyle: { color: '#3b82f6' }
        },
        {
          name: t('pages.dashboard.throughput.legend.unknown'),
          type: 'line',
          smooth: false,
          showSymbol: false,
          symbol: 'none',
          data: seriesUnknown,
          lineStyle: { color: '#22c55e', width: 1.2 },
          itemStyle: { color: '#22c55e' }
        },
        {
          name: t('pages.dashboard.throughput.legend.total'),
          type: 'line',
          smooth: false,
          showSymbol: false,
          symbol: 'none',
          data: seriesTotal,
          lineStyle: { color: '#6b7280', width: 1.1 },
          itemStyle: { color: '#6b7280' }
        }
      ]
      };
    },
    [isDark, seriesKnown, seriesTotal, seriesUnknown, t]
  );

  const topItems = topQuery.data?.items ?? [];
  const topSeries = buildTopSeries(topItems);
  const topOption = useMemo(
    () => ({
      tooltip: { trigger: 'item' },
      grid: { left: 16, right: 16, top: 20, bottom: 20, containLabel: true },
      xAxis: { type: 'value' },
      yAxis: {
        type: 'category',
        data: topSeries.map((item) => item.name),
        axisLabel: { width: 140, overflow: 'truncate' }
      },
      series: [
        {
          type: 'bar',
          data: topSeries.map((item) => item.value),
          itemStyle: { color: '#f36f21' }
        }
      ]
    }),
    [topSeries]
  );

  const healthItems = cameraHealthQuery.data?.items ?? [];
  const offlineCount = healthItems.filter((item) => item.state === 'OFFLINE').length;
  const warnCount = healthItems.filter((item) => item.state === 'WARN').length;
  const disabledCount = healthItems.filter((item) => item.state === 'DISABLED').length;

  const alerts = alertsQuery.data?.items ?? [];
  const faceImageByEventId = useMemo(
    () => faceImageMapRef.current,
    [faceImageVersion]
  );
  const recentAlerts = alerts.slice(0, 5);
  const bestshotQueries = useQueries({
    queries: recentAlerts.map((alert) => ({
      queryKey: ['face-event-bestshot', alert.id],
      queryFn: () => getFaceEventBestshot(alert.id),
      enabled: !faceImageByEventId.has(alert.id)
    }))
  });
  const recentDetections = recentAlerts.map((alert, index) => {
    const liveImage = faceImageByEventId.get(alert.id);
    const bestshot = bestshotQueries[index]?.data?.base64 ?? null;
    return {
      alert,
      imageBase64: liveImage ?? bestshot
    };
  });
  const systemMetrics = systemQuery.data;

  return (
    <Stack gap="lg" className="page">
      <Group justify="space-between" align="flex-start" wrap="wrap">
        <Stack gap={4}>
          <Text size="sm" className="muted-text">
            {t('pages.dashboard.subtitle')}
          </Text>
          <Text size="xl" fw={600}>
            {t('pages.dashboard.title')}
          </Text>
        </Stack>
        <Group gap="md" align="center">
          <ActionIcon
            variant="light"
            onClick={handleRefreshNow}
            aria-label={t('pages.dashboard.actions.refreshNow')}
          >
            <IconRefresh size={16} />
          </ActionIcon>
          <SegmentedControl
            data={[
              ...RANGE_PRESETS.map((preset) => ({ value: preset.value, label: preset.label }))
            ]}
            value={rangeKey}
            onChange={setRangeKey}
          />
          <Switch
            checked={autoRefresh}
            onChange={(event) => setAutoRefresh(event.currentTarget.checked)}
            label={t('pages.dashboard.actions.autoRefresh')}
          />
        </Group>
      </Group>

      <Grid gutter="lg">
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <Paper p="lg" radius="lg" className="surface-card">
            <Group justify="space-between" align="center">
              <Stack gap={4}>
                <Text size="sm" className="muted-text">
                  {t('pages.dashboard.throughput.subtitle')}
                </Text>
                <Text size="lg" fw={600}>
                  {t('pages.dashboard.throughput.title')}
                </Text>
              </Stack>
            </Group>
            <Box mt="md" style={{ height: 280 }}>
              <ReactECharts option={throughputOption} style={{ height: '100%' }} />
            </Box>
          </Paper>
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 4 }}>
          <Paper p="lg" radius="lg" className="surface-card" style={{ height: '100%' }}>
            <Stack gap={4}>
              <Text size="sm" className="muted-text">
                {t('pages.dashboard.topCameras.subtitle')}
              </Text>
              <Text size="lg" fw={600}>
                {t('pages.dashboard.topCameras.title')}
              </Text>
            </Stack>
            <Box mt="md" style={{ height: 280 }}>
              <ReactECharts option={topOption} style={{ height: '100%' }} />
            </Box>
          </Paper>
        </Grid.Col>
      </Grid>

      <Grid gutter="lg">
        <Grid.Col span={{ base: 12, xl: 8 }}>
          <DashboardMapPanel autoRefresh={autoRefresh} />
        </Grid.Col>
        <Grid.Col span={{ base: 12, xl: 4 }}>
          <Paper p="lg" radius="lg" className="surface-card" style={{ height: '100%', minHeight: 0 }}>
            <Group justify="space-between" align="center">
              <Stack gap={4}>
                <Text size="sm" className="muted-text">
                  {t('pages.dashboard.alerts.subtitle')}
                </Text>
                <Text size="lg" fw={600}>
                  {t('pages.dashboard.alerts.title')}
                </Text>
              </Stack>
              <Badge variant="light" color="brand">
                {recentAlerts.length}
              </Badge>
            </Group>
            <Divider my="md" />
            <ScrollArea h="calc(100vh - 360px)" type="auto">
              <Stack gap="md">
                {recentDetections.length === 0 && (
                  <Text size="sm" className="muted-text">
                    {t('pages.dashboard.alerts.empty')}
                  </Text>
                )}
                {recentDetections.map(({ alert, imageBase64 }) => (
                  <AlertRow key={alert.id} alert={alert} imageBase64={imageBase64} />
                ))}
              </Stack>
            </ScrollArea>
          </Paper>
        </Grid.Col>
      </Grid>

      <SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }} spacing="lg">
        <MetricCard
          label={t('pages.dashboard.cards.totalEvents')}
          value={formatValue(summary?.totalEvents, locale)}
          icon={<IconActivity size={20} />}
        />
        <MetricCard
          label={t('pages.dashboard.cards.watchlistMatches')}
          value={formatValue(summary?.matchCount, locale)}
          icon={<IconAlertTriangle size={20} />}
          accent="amber"
        />
        <MetricCard
          label={t('pages.dashboard.cards.unknownFaces')}
          value={formatValue(summary?.unknownCount, locale)}
          icon={<IconEyeOff size={20} />}
          accent="amber"
        />
        <MetricCard
          label={t('pages.dashboard.cards.knownFaces')}
          value={formatValue(summary?.knownCount, locale)}
          icon={<IconShieldCheck size={20} />}
        />
        <MetricCard
          label={t('pages.dashboard.cards.enabledCameras')}
          value={formatValue(summary?.enabledCameras, locale)}
          icon={<IconCamera size={20} />}
        />
        <MetricCard
          label={t('pages.dashboard.cards.offlineCameras')}
          value={formatValue(offlineCount, locale)}
          icon={<IconWifiOff size={20} />}
          accent="amber"
        />
        <MetricCard
          label={t('pages.dashboard.cards.activeCameras')}
          value={formatValue(summary?.activeCameras, locale)}
          icon={<IconBolt size={20} />}
        />
        <MetricCard
          label={t('pages.dashboard.cards.webhookFails')}
          value={formatValue(systemMetrics?.webhookFailTotal ?? null, locale)}
          icon={<IconClock size={20} />}
          accent="amber"
        />
      </SimpleGrid>

      <Paper p="lg" radius="lg" className="surface-card">
        <Group justify="space-between" align="center">
          <Stack gap={4}>
            <Text size="sm" className="muted-text">
              {t('pages.dashboard.systemHealth.subtitle')}
            </Text>
            <Text size="lg" fw={600}>
              {t('pages.dashboard.systemHealth.title')}
            </Text>
          </Stack>
          <Group gap="xs">
            <Badge variant="light" color="orange">
              {t('pages.dashboard.systemHealth.badges.offline', { count: offlineCount })}
            </Badge>
            <Badge variant="light" color="yellow">
              {t('pages.dashboard.systemHealth.badges.warn', { count: warnCount })}
            </Badge>
            <Badge variant="light" color="gray">
              {t('pages.dashboard.systemHealth.badges.disabled', { count: disabledCount })}
            </Badge>
          </Group>
        </Group>
        <Divider my="md" />
        <SimpleGrid cols={{ base: 1, md: 3 }} spacing="lg">
          <Stack gap="sm">
            <Text size="sm" className="muted-text">
              {t('pages.dashboard.systemHealth.queueDepth')}
            </Text>
            <Group justify="space-between">
              <Text size="sm" fw={600}>
                {t('pages.dashboard.systemHealth.ingest')}
              </Text>
              <Text size="sm">{formatValue(systemMetrics?.queueIngest ?? null, locale)}</Text>
            </Group>
            <Progress
              value={Math.min(100, (systemMetrics?.queueIngest ?? 0) * 5)}
              color="orange"
            />
            <Group justify="space-between">
              <Text size="sm" fw={600}>
                {t('pages.dashboard.systemHealth.webhook')}
              </Text>
              <Text size="sm">{formatValue(systemMetrics?.queueWebhook ?? null, locale)}</Text>
            </Group>
            <Progress
              value={Math.min(100, (systemMetrics?.queueWebhook ?? 0) * 5)}
              color="orange"
            />
          </Stack>
          <Stack gap="sm">
            <Text size="sm" className="muted-text">
              {t('pages.dashboard.systemHealth.dropReasons')}
            </Text>
            {Object.keys(systemMetrics?.ingestDroppedByReason ?? {}).length === 0 && (
              <Text size="sm" className="muted-text">
                {t('pages.dashboard.systemHealth.noDrops')}
              </Text>
            )}
            {Object.entries(systemMetrics?.ingestDroppedByReason ?? {}).map(([reason, value]) => (
              <Group key={reason} justify="space-between">
                <Text size="sm">{reason}</Text>
                <Text size="sm" fw={600}>
                  {formatValue(value, locale)}
                </Text>
              </Group>
            ))}
          </Stack>
          <Stack gap="sm">
            <Text size="sm" className="muted-text">
              {t('pages.dashboard.systemHealth.latency')}
            </Text>
            <Group justify="space-between">
              <Text size="sm">{t('pages.dashboard.systemHealth.matchP95')}</Text>
              <Text size="sm" fw={600}>
                {systemMetrics?.matchLatencyP95Seconds?.toFixed(2) ?? '-'}
              </Text>
            </Group>
            <Group justify="space-between">
              <Text size="sm">{t('pages.dashboard.systemHealth.webhookSuccess')}</Text>
              <Text size="sm" fw={600}>
                {formatValue(systemMetrics?.webhookSuccessTotal ?? null, locale)}
              </Text>
            </Group>
            <Group justify="space-between">
              <Text size="sm">{t('pages.dashboard.systemHealth.webhookFail')}</Text>
              <Text size="sm" fw={600}>
                {formatValue(systemMetrics?.webhookFailTotal ?? null, locale)}
              </Text>
            </Group>
          </Stack>
        </SimpleGrid>
      </Paper>
    </Stack>
  );
}

function AlertRow({
  alert,
  imageBase64
}: {
  alert: DashboardAlertItem;
  imageBase64?: string | null;
}) {
  const { t } = useI18n();
  const severityColor = resolveSeverityColor(alert.severity);
  const label = alert.cameraCode ?? alert.cameraId;
  const personLabel =
    alert.personName ??
    (alert.isKnown ? t('pages.dashboard.alerts.knownPerson') : t('pages.dashboard.alerts.unknown'));
  const severityLabel = t(
    `common.severity.${alert.severity.toLowerCase()}`,
    undefined,
    alert.severity
  );
  const faceImage = resolveFaceImage(imageBase64);

  return (
    <Paper p="sm" radius="md" className="surface-card strong">
      <Group align="flex-start" wrap="nowrap">
        <Box>
          {faceImage ? (
            <Image src={faceImage} w={72} h={72} radius="md" fit="cover" />
          ) : (
            <Center
              w={72}
              h={72}
              className="surface-card"
              style={{ borderRadius: 10, borderStyle: 'dashed' }}
            >
              <Text size="xs" className="muted-text" ta="center">
                {t('common.empty.noImage')}
              </Text>
            </Center>
          )}
        </Box>
        <Stack gap={6} style={{ flex: 1 }}>
          <Group justify="space-between" align="center">
            <Stack gap={2}>
              <Text size="sm" fw={600}>
                {personLabel}
              </Text>
              <Text size="xs" className="muted-text">
                {t('pages.dashboard.alerts.eventAt', {
                  label,
                  time: formatDateTime(alert.eventTimeUtc, t)
                })}
              </Text>
            </Stack>
            <Badge variant="light" color={severityColor}>
              {severityLabel}
            </Badge>
          </Group>
          <Divider my={2} />
          <Group justify="space-between">
            <Text size="xs" className="muted-text">
              {t('common.fields.similarity')}
            </Text>
            <Text size="xs" fw={600}>
              {alert.similarity?.toFixed(2) ?? '-'}
            </Text>
          </Group>
          <Group justify="space-between">
            <Text size="xs" className="muted-text">
              {t('common.fields.score')}
            </Text>
            <Text size="xs" fw={600}>
              {alert.score?.toFixed(2) ?? '-'}
            </Text>
          </Group>
        </Stack>
      </Group>
    </Paper>
  );
}

