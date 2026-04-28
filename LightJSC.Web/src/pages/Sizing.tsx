import {
  Alert,
  Badge,
  Button,
  Collapse,
  Divider,
  Group,
  NumberInput,
  Paper,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Switch,
  Table,
  Text,
  ThemeIcon
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import {
  IconAlertTriangle,
  IconCalculator,
  IconChevronDown,
  IconChevronUp,
  IconCpu,
  IconDatabase,
  IconDeviceDesktop,
  IconDownload,
  IconInfoCircle,
  IconRefresh,
  IconServer
} from '@tabler/icons-react';
import { type ReactNode, useMemo, useState } from 'react';
import { useI18n } from '../i18n/I18nProvider';
import {
  applyPresetValues,
  calculateSizing,
  type ArchitectureMode,
  DEFAULT_SIZING_INPUTS,
  type ClientRecommendation,
  type NodeRecommendation,
  SIZING_PRESETS,
  type SizingInputs,
  type SizingTier,
  type WarningSeverity
} from '../utils/sizing';

const formatDecimal = (value: number, language: 'vi' | 'en', digits = 2) =>
  new Intl.NumberFormat(language === 'vi' ? 'vi-VN' : 'en-US', {
    minimumFractionDigits: 0,
    maximumFractionDigits: digits
  }).format(value);

const formatInteger = (value: number, language: 'vi' | 'en') =>
  new Intl.NumberFormat(language === 'vi' ? 'vi-VN' : 'en-US', {
    maximumFractionDigits: 0
  }).format(value);

const triggerDownload = (content: string, fileName: string, mimeType: string) => {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  URL.revokeObjectURL(url);
};

type ExportSystemRow = {
  index: number;
  system: string;
  quantity: number;
  cpuCores: number;
  ram: string;
  storage: string;
  notes: string;
};

type WorkstationSummary = {
  count: number;
  cpuLabel: string;
  ramGb: number;
  storageGb: number;
};

const getServerDataStorageLabel = (
  label: NodeRecommendation['dataStorageLabel'],
  t: (key: string, params?: Record<string, string | number>, fallback?: string) => string
) => {
  if (label === 'bestshot') {
    return t('pages.sizing.results.server.storageParts.bestshot');
  }

  if (label === 'database') {
    return t('pages.sizing.results.server.storageParts.database');
  }

  if (label === 'mixed') {
    return t('pages.sizing.results.server.storageParts.mixed');
  }

  return t('pages.sizing.results.server.storageParts.data');
};

const getSeverityColor = (severity: WarningSeverity) => {
  if (severity === 'critical') return 'red';
  if (severity === 'warn') return 'orange';
  return 'blue';
};

const getTierColor = (tier: SizingTier) => {
  if (tier === 'pilot') return 'blue';
  if (tier === 'standard') return 'teal';
  if (tier === 'large') return 'orange';
  return 'red';
};

const getArchitectureColor = (mode: ArchitectureMode) => {
  if (mode === 'allInOne') return 'blue';
  if (mode === 'splitDatabase') return 'teal';
  if (mode === 'splitRealtime') return 'orange';
  return 'red';
};

const buildExportSystemRows = (
  result: ReturnType<typeof calculateSizing>,
  t: (key: string, params?: Record<string, string | number>, fallback?: string) => string
): ExportSystemRow[] => {
  const serverCount = result.nodes.reduce((sum, item) => sum + item.count, 0);
  const serverCpu = result.nodes.reduce((max, item) => Math.max(max, item.cpuCores), 0);
  const serverRam = result.nodes.reduce((max, item) => Math.max(max, item.ramGb), 0);
  const serverSystemStorage = result.nodes.reduce(
    (max, item) => Math.max(max, item.systemStorageGb),
    0
  );
  const serverDataStorage = result.nodes.reduce((max, item) => Math.max(max, item.dataStorageGb), 0);

  const clientCount = result.clients.reduce((sum, item) => sum + item.count, 0);
  const clientCpu = result.clients.reduce((max, item) => Math.max(max, item.cpuCores), 0);
  const clientRam = result.clients.reduce((max, item) => Math.max(max, item.ramGb), 0);
  const clientStorage = result.clients.reduce((max, item) => Math.max(max, item.storageGb), 0);

  return [
    {
      index: 1,
      system: t('pages.sizing.export.system.server'),
      quantity: serverCount,
      cpuCores: serverCpu,
      ram: serverRam > 0 ? `${serverRam} GB` : '-',
      storage:
        serverSystemStorage > 0 || serverDataStorage > 0
          ? `${t('pages.sizing.results.server.storageParts.system')} ${serverSystemStorage} GB + ${t('pages.sizing.results.server.storageParts.data')} ${serverDataStorage} GB`
          : '-',
      notes: t(`pages.sizing.architectures.${result.architecture}.label`)
    },
    {
      index: 2,
      system: t('pages.sizing.export.system.workstation'),
      quantity: clientCount,
      cpuCores: clientCpu,
      ram: clientRam > 0 ? `${clientRam} GB` : '-',
      storage: clientStorage > 0 ? `${clientStorage} GB` : '-',
      notes:
        clientCount > 0
          ? t('pages.sizing.export.notes.clientSummary', { count: clientCount })
          : t('pages.sizing.export.notes.noClient')
    }
  ];
};

function KpiCard({
  icon,
  label,
  value,
  caption
}: {
  icon: ReactNode;
  label: string;
  value: string;
  caption?: string;
}) {
  return (
    <Paper p="md" radius="xl" className="surface-card strong sizing-kpi-card">
      <Group justify="space-between" align="flex-start" wrap="nowrap">
        <Stack gap={6}>
          <Text size="sm" className="muted-text">
            {label}
          </Text>
          <Text fw={800} className="sizing-kpi-value">
            {value}
          </Text>
          {caption ? (
            <Text size="xs" className="muted-text">
              {caption}
            </Text>
          ) : null}
        </Stack>
        <ThemeIcon size={42} radius="xl" variant="light" color="brand">
          {icon}
        </ThemeIcon>
      </Group>
    </Paper>
  );
}

function ServerNodeTable({
  rows,
  t
}: {
  rows: NodeRecommendation[];
  t: (key: string, params?: Record<string, string | number>, fallback?: string) => string;
}) {
  return (
    <Table highlightOnHover withTableBorder verticalSpacing="sm">
      <Table.Thead>
        <Table.Tr>
          <Table.Th>{t('pages.sizing.results.server.columns.role')}</Table.Th>
          <Table.Th ta="center">{t('pages.sizing.results.server.columns.count')}</Table.Th>
          <Table.Th ta="center">{t('pages.sizing.results.server.columns.cpu')}</Table.Th>
          <Table.Th ta="center">{t('pages.sizing.results.server.columns.ram')}</Table.Th>
          <Table.Th ta="center">{t('pages.sizing.results.server.columns.storage')}</Table.Th>
          <Table.Th>{t('pages.sizing.results.server.columns.notes')}</Table.Th>
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {rows.map((row) => (
          <Table.Tr key={`${row.role}-${row.count}`}>
            <Table.Td>{t(`pages.sizing.results.server.roles.${row.role}`)}</Table.Td>
            <Table.Td ta="center">{row.count}</Table.Td>
            <Table.Td ta="center">{row.cpuCores}</Table.Td>
            <Table.Td ta="center">{row.ramGb} GB</Table.Td>
            <Table.Td ta="center">
              <Stack gap={2} align="center">
                <Text size="sm" fw={700}>
                  {row.storageGb} GB
                </Text>
                <Text size="xs" className="muted-text">
                  {t('pages.sizing.results.server.storageParts.system')}: {row.systemStorageGb} GB
                </Text>
                {row.dataStorageGb > 0 ? (
                  <Text size="xs" className="muted-text">
                    {getServerDataStorageLabel(row.dataStorageLabel, t)}: {row.dataStorageGb} GB
                  </Text>
                ) : null}
              </Stack>
            </Table.Td>
            <Table.Td>
              <Group gap={6}>
                {row.notes.map((note) => (
                  <Badge key={note} variant="light" color="gray">
                    {t(`pages.sizing.notes.${note}`)}
                  </Badge>
                ))}
              </Group>
            </Table.Td>
          </Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  );
}

const buildWorkstationSummary = (clients: ClientRecommendation[]): WorkstationSummary | null => {
  if (clients.length === 0) {
    return null;
  }

  const totalCount = clients.reduce((sum, item) => sum + item.count, 0);
  const highest = clients.reduce((best, current) => {
    if (current.cpuCores !== best.cpuCores) {
      return current.cpuCores > best.cpuCores ? current : best;
    }

    if (current.ramGb !== best.ramGb) {
      return current.ramGb > best.ramGb ? current : best;
    }

    if (current.storageGb !== best.storageGb) {
      return current.storageGb > best.storageGb ? current : best;
    }

    return best;
  });

  return {
    count: totalCount,
    cpuLabel: highest.cpuLabel,
    ramGb: highest.ramGb,
    storageGb: highest.storageGb
  };
};

export function Sizing() {
  const { t, language } = useI18n();
  const [inputs, setInputs] = useState<SizingInputs>(DEFAULT_SIZING_INPUTS);
  const [advancedOpened, setAdvancedOpened] = useState(false);

  const result = useMemo(() => calculateSizing(inputs), [inputs]);
  const workstationSummary = useMemo(() => buildWorkstationSummary(result.clients), [result.clients]);

  const updateNumber = (field: keyof SizingInputs, value: string | number) => {
    const nextValue = typeof value === 'number' && Number.isFinite(value) ? value : 0;
    setInputs((current) => ({ ...current, [field]: nextValue }));
  };

  const updateBoolean = (field: keyof SizingInputs, value: boolean) => {
    setInputs((current) => ({ ...current, [field]: value }));
  };

  const handlePresetChange = (value: string | null) => {
    if (!value) return;
    setInputs((current) => applyPresetValues(current, value as SizingInputs['presetId']));
  };

  const exportCsv = () => {
    const exportRows = buildExportSystemRows(result, t);
    const rows = [
      `"${t('pages.sizing.export.columns.index')}","${t('pages.sizing.export.columns.system')}","${t('pages.sizing.export.columns.quantity')}","${t('pages.sizing.export.columns.cpu')}","${t('pages.sizing.export.columns.ram')}","${t('pages.sizing.export.columns.storage')}","${t('pages.sizing.export.columns.notes')}"`
    ];
    exportRows.forEach((row) => {
      rows.push(
        `"${row.index}","${row.system}","${row.quantity}","${row.cpuCores}","${row.ram}","${row.storage}","${row.notes}"`
      );
    });

    triggerDownload(
      `\uFEFF${rows.join('\n')}`,
      `lightjsc-sizing-${new Date().toISOString().slice(0, 10)}.csv`,
      'text/csv;charset=utf-8'
    );
  };

  const exportXlsx = async () => {
    try {
      const [{ utils, writeFileXLSX }] = await Promise.all([import('xlsx')]);
      const exportRows = buildExportSystemRows(result, t).map((row) => ({
        [t('pages.sizing.export.columns.index')]: row.index,
        [t('pages.sizing.export.columns.system')]: row.system,
        [t('pages.sizing.export.columns.quantity')]: row.quantity,
        [t('pages.sizing.export.columns.cpu')]: row.cpuCores,
        [t('pages.sizing.export.columns.ram')]: row.ram,
        [t('pages.sizing.export.columns.storage')]: row.storage,
        [t('pages.sizing.export.columns.notes')]: row.notes
      }));
      const workbook = utils.book_new();
      const worksheet = utils.json_to_sheet(exportRows);
      utils.book_append_sheet(workbook, worksheet, t('pages.sizing.export.sheetName'));
      writeFileXLSX(workbook, `lightjsc-sizing-${new Date().toISOString().slice(0, 10)}.xlsx`);
    } catch (error) {
      notifications.show({
        color: 'red',
        title: t('pages.sizing.notifications.exportXlsxFailed.title'),
        message:
          error instanceof Error
            ? error.message
            : t('pages.sizing.notifications.exportXlsxFailed.message')
      });
    }
  };

  const resetInputs = () => {
    setInputs(DEFAULT_SIZING_INPUTS);
    notifications.show({
      color: 'brand',
      title: t('pages.sizing.notifications.reset.title'),
      message: t('pages.sizing.notifications.reset.message')
    });
  };

  return (
    <Stack gap="lg" className="page sizing-page">
      <Group justify="space-between" align="flex-start" wrap="wrap">
        <div>
          <Text size="sm" className="muted-text">
            {t('pages.sizing.subtitle')}
          </Text>
          <Text component="h1" size="2rem" fw={800}>
            {t('pages.sizing.title')}
          </Text>
        </div>
        <Group gap="sm">
          <Button variant="light" color="gray" leftSection={<IconRefresh size={16} />} onClick={resetInputs}>
            {t('pages.sizing.actions.reset')}
          </Button>
          <Button variant="light" color="teal" leftSection={<IconDownload size={16} />} onClick={() => void exportXlsx()}>
            {t('pages.sizing.actions.exportXlsx')}
          </Button>
          <Button variant="light" color="brand" leftSection={<IconDownload size={16} />} onClick={exportCsv}>
            {t('pages.sizing.actions.exportCsv')}
          </Button>
        </Group>
      </Group>

      <Alert variant="light" color="blue" radius="xl" icon={<IconInfoCircle size={18} />} className="surface-card">
        <Text fw={700}>{t('pages.sizing.info.title')}</Text>
        <Text size="sm" mt={4}>
          {t('pages.sizing.info.message')}
        </Text>
      </Alert>

      <Paper p="lg" radius="2rem" className="surface-card strong">
        <Stack gap="lg">
          <Group gap="md" align="center" wrap="wrap">
            <ThemeIcon size={44} radius="xl" variant="light" color="brand">
              <IconCalculator size={22} />
            </ThemeIcon>
            <div>
              <Text fw={800}>{t('pages.sizing.inputs.title')}</Text>
              <Text size="sm" className="muted-text">
                {t('pages.sizing.inputs.subtitle')}
              </Text>
            </div>
          </Group>
          <SimpleGrid cols={{ base: 1, md: 4 }} spacing="md">
            <Select
              label={t('pages.sizing.inputs.preset')}
              data={SIZING_PRESETS.map((preset) => ({
                value: preset.id,
                label: t(`pages.sizing.presets.${preset.id}.label`)
              }))}
              value={inputs.presetId}
              onChange={handlePresetChange}
            />
            <NumberInput
              label={t('pages.sizing.inputs.siteCount')}
              value={inputs.siteCount}
              min={1}
              onChange={(value) => updateNumber('siteCount', value)}
            />
            <NumberInput
              label={t('pages.sizing.inputs.cameraCount')}
              value={inputs.cameraCount}
              min={0}
              onChange={(value) => updateNumber('cameraCount', value)}
            />
            <NumberInput
              label={t('pages.sizing.inputs.activeHours')}
              value={inputs.activeHoursPerDay}
              min={1}
              max={24}
              onChange={(value) => updateNumber('activeHoursPerDay', value)}
            />
          </SimpleGrid>

          <Divider label={t('pages.sizing.sections.storage')} labelPosition="left" />

          <SimpleGrid cols={{ base: 1, md: 4 }} spacing="md">
            <NumberInput
              label={t('pages.sizing.inputs.retentionDays')}
              value={inputs.retentionDays}
              min={1}
              onChange={(value) => updateNumber('retentionDays', value)}
            />
            <NumberInput
              label={t('pages.sizing.inputs.avgBestshotKb')}
              value={inputs.avgBestshotKb}
              min={1}
              onChange={(value) => updateNumber('avgBestshotKb', value)}
            />
            <NumberInput
              label={t('pages.sizing.inputs.avgDbKb')}
              value={inputs.avgDbKb}
              min={1}
              onChange={(value) => updateNumber('avgDbKb', value)}
            />
            <NumberInput
              label={t('pages.sizing.inputs.avgRealtimePayloadKb')}
              value={inputs.avgRealtimePayloadKb}
              min={1}
              onChange={(value) => updateNumber('avgRealtimePayloadKb', value)}
            />
          </SimpleGrid>

          <SimpleGrid cols={{ base: 1, md: 4 }} spacing="md">
            <Switch
              checked={inputs.saveKnown}
              onChange={(event) => updateBoolean('saveKnown', event.currentTarget.checked)}
              label={t('pages.sizing.inputs.saveKnown')}
            />
            <Switch
              checked={inputs.saveUnknown}
              onChange={(event) => updateBoolean('saveUnknown', event.currentTarget.checked)}
              label={t('pages.sizing.inputs.saveUnknown')}
            />
            <Switch
              checked={inputs.saveThumb}
              onChange={(event) => updateBoolean('saveThumb', event.currentTarget.checked)}
              label={t('pages.sizing.inputs.saveThumb')}
            />
            <Switch
              checked={inputs.standbyNode}
              onChange={(event) => updateBoolean('standbyNode', event.currentTarget.checked)}
              label={t('pages.sizing.inputs.standbyNode')}
            />
          </SimpleGrid>

          <Button
            variant="subtle"
            color="brand"
            leftSection={advancedOpened ? <IconChevronUp size={16} /> : <IconChevronDown size={16} />}
            onClick={() => setAdvancedOpened((current) => !current)}
            style={{ alignSelf: 'flex-start' }}
          >
            {advancedOpened
              ? t('pages.sizing.actions.hideAdvanced')
              : t('pages.sizing.actions.showAdvanced')}
          </Button>

          <Text size="sm" className="muted-text">
            {t('pages.sizing.inputs.advancedHint')}
          </Text>

          <Collapse in={advancedOpened}>
            <Stack gap="lg">
              <Divider label={t('pages.sizing.sections.workload')} labelPosition="left" />

              <SimpleGrid cols={{ base: 1, md: 4 }} spacing="md">
                <NumberInput
                  label={t('pages.sizing.inputs.avgEventsPerMinute')}
                  value={inputs.avgEventsPerMinute}
                  min={0}
                  decimalScale={2}
                  onChange={(value) => updateNumber('avgEventsPerMinute', value)}
                />
                <NumberInput
                  label={t('pages.sizing.inputs.peakEventsPerMinute')}
                  value={inputs.peakEventsPerMinute}
                  min={0}
                  decimalScale={2}
                  onChange={(value) => updateNumber('peakEventsPerMinute', value)}
                />
                <NumberInput
                  label={t('pages.sizing.inputs.knownRatio')}
                  value={inputs.knownRatio * 100}
                  min={0}
                  max={100}
                  suffix="%"
                  onChange={(value) =>
                    updateNumber('knownRatio', ((typeof value === 'number' ? value : 0) || 0) / 100)
                  }
                />
                <NumberInput
                  label={t('pages.sizing.inputs.webhookSubscribers')}
                  value={inputs.webhookSubscribers}
                  min={0}
                  onChange={(value) => updateNumber('webhookSubscribers', value)}
                />
              </SimpleGrid>

              <Divider label={t('pages.sizing.sections.watchlist')} labelPosition="left" />

              <SimpleGrid cols={{ base: 1, md: 4 }} spacing="md">
                <NumberInput
                  label={t('pages.sizing.inputs.personCount')}
                  value={inputs.personCount}
                  min={0}
                  onChange={(value) => updateNumber('personCount', value)}
                />
                <NumberInput
                  label={t('pages.sizing.inputs.templatesPerPerson')}
                  value={inputs.templatesPerPerson}
                  min={1}
                  onChange={(value) => updateNumber('templatesPerPerson', value)}
                />
                <NumberInput
                  label={t('pages.sizing.inputs.activeTemplateRatio')}
                  value={inputs.activeTemplateRatio * 100}
                  min={5}
                  max={100}
                  suffix="%"
                  onChange={(value) =>
                    updateNumber(
                      'activeTemplateRatio',
                      ((typeof value === 'number' ? value : 0) || 0) / 100
                    )
                  }
                />
                <NumberInput
                  label={t('pages.sizing.inputs.growthPercent12m')}
                  value={inputs.growthPercent12m}
                  min={0}
                  suffix="%"
                  onChange={(value) => updateNumber('growthPercent12m', value)}
                />
              </SimpleGrid>

              <Divider label={t('pages.sizing.sections.webUsers')} labelPosition="left" />

              <SimpleGrid cols={{ base: 1, md: 4 }} spacing="md">
                <NumberInput
                  label={t('pages.sizing.inputs.dashboardUsers')}
                  value={inputs.dashboardUsers}
                  min={0}
                  onChange={(value) => updateNumber('dashboardUsers', value)}
                />
                <NumberInput
                  label={t('pages.sizing.inputs.faceStreamUsers')}
                  value={inputs.faceStreamUsers}
                  min={0}
                  onChange={(value) => updateNumber('faceStreamUsers', value)}
                />
                <NumberInput
                  label={t('pages.sizing.inputs.welcomeUsers')}
                  value={inputs.welcomeUsers}
                  min={0}
                  onChange={(value) => updateNumber('welcomeUsers', value)}
                />
                <NumberInput
                  label={t('pages.sizing.inputs.attendanceUsers')}
                  value={inputs.attendanceUsers}
                  min={0}
                  onChange={(value) => updateNumber('attendanceUsers', value)}
                />
              </SimpleGrid>

              <SimpleGrid cols={{ base: 1, md: 4 }} spacing="md">
                <NumberInput
                  label={t('pages.sizing.inputs.enrollmentStations')}
                  value={inputs.enrollmentStations}
                  min={0}
                  onChange={(value) => updateNumber('enrollmentStations', value)}
                />
                <NumberInput
                  label={t('pages.sizing.inputs.adminUsers')}
                  value={inputs.adminUsers}
                  min={0}
                  onChange={(value) => updateNumber('adminUsers', value)}
                />
                <NumberInput
                  label={t('pages.sizing.inputs.liveVideoOperators')}
                  value={inputs.liveVideoOperators}
                  min={0}
                  onChange={(value) => updateNumber('liveVideoOperators', value)}
                />
                <Switch
                  checked={inputs.futureLiveView}
                  onChange={(event) => updateBoolean('futureLiveView', event.currentTarget.checked)}
                  label={t('pages.sizing.inputs.futureLiveView')}
                />
              </SimpleGrid>
            </Stack>
          </Collapse>
        </Stack>
      </Paper>

      <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="md">
        <KpiCard
          icon={<IconServer size={20} />}
          label={t('pages.sizing.kpis.eventsPerDay')}
          value={formatInteger(result.metrics.eventsPerDay, language)}
          caption={t('pages.sizing.kpis.storedEventsCaption', {
            value: formatInteger(result.metrics.storedEventsPerDay, language)
          })}
        />
        <KpiCard
          icon={<IconDatabase size={20} />}
          label={t('pages.sizing.kpis.storage')}
          value={`${formatDecimal(result.metrics.usableStorageTb, language)} TB`}
          caption={t('pages.sizing.kpis.storageCaption', {
            value: formatDecimal(result.metrics.totalStorageGbPerDay, language)
          })}
        />
      </SimpleGrid>

      <SimpleGrid cols={{ base: 1, xl: 3 }} spacing="lg">
        <Paper p="lg" radius="2rem" className="surface-card strong" style={{ gridColumn: 'span 2' }}>
          <Stack gap="lg">
            <Group justify="space-between" wrap="wrap">
              <div>
                <Text size="sm" className="muted-text">
                  {t('pages.sizing.results.summary.subtitle')}
                </Text>
                <Text fw={800}>{t('pages.sizing.results.summary.title')}</Text>
              </div>
              <Group gap="xs">
                <Badge color={getTierColor(result.tier)} variant="light" size="lg">
                  {t(`pages.sizing.tiers.${result.tier}.label`)}
                </Badge>
                <Badge color={getArchitectureColor(result.architecture)} variant="light" size="lg">
                  {t(`pages.sizing.architectures.${result.architecture}.label`)}
                </Badge>
              </Group>
            </Group>

            <Alert
              variant="light"
              color={getArchitectureColor(result.architecture)}
              radius="xl"
              icon={<IconServer size={18} />}
            >
              <Text fw={700}>{t(`pages.sizing.architectures.${result.architecture}.title`)}</Text>
              <Text size="sm" mt={4}>
                {t(`pages.sizing.architectures.${result.architecture}.description`)}
              </Text>
            </Alert>

            <Paper p="md" radius="xl" className="surface-card sizing-result-card">
              <Group justify="space-between">
                <Text fw={700}>{t('pages.sizing.results.summary.current')}</Text>
                <ThemeIcon variant="light" color="brand">
                  <IconCpu size={16} />
                </ThemeIcon>
              </Group>
              <Stack gap={8} mt="sm">
                <Group justify="space-between">
                  <Text size="sm" className="muted-text">
                    {t('pages.sizing.results.summary.cameraCount')}
                  </Text>
                  <Text fw={700}>{formatInteger(inputs.cameraCount, language)}</Text>
                </Group>
                <Group justify="space-between">
                  <Text size="sm" className="muted-text">
                    {t('pages.sizing.results.summary.activeTemplates')}
                  </Text>
                  <Text fw={700}>{formatInteger(result.metrics.activeTemplates, language)}</Text>
                </Group>
                <Group justify="space-between">
                  <Text size="sm" className="muted-text">
                    {t('pages.sizing.results.summary.recommendedNic')}
                  </Text>
                  <Text fw={700}>{result.metrics.recommendedNicGbps} GbE</Text>
                </Group>
                <Group justify="space-between">
                  <Text size="sm" className="muted-text">
                    {t('pages.sizing.results.summary.ingest')}
                  </Text>
                  <Text fw={700}>{formatDecimal(result.metrics.ingestMbps, language)} Mbps</Text>
                </Group>
              </Stack>
            </Paper>
          </Stack>
        </Paper>

        <Paper p="lg" radius="2rem" className="surface-card strong">
          <Stack gap="md">
            <div>
              <Text size="sm" className="muted-text">
                {t('pages.sizing.results.warnings.subtitle')}
              </Text>
              <Text fw={800}>{t('pages.sizing.results.warnings.title')}</Text>
            </div>

            {result.warnings.map((warning) => (
              <Alert
                key={warning.code}
                variant="light"
                color={getSeverityColor(warning.severity)}
                radius="xl"
                icon={<IconAlertTriangle size={18} />}
              >
                <Text fw={700}>{t(`pages.sizing.warnings.${warning.code}.title`)}</Text>
                <Text size="sm" mt={4}>
                  {t(`pages.sizing.warnings.${warning.code}.message`)}
                </Text>
              </Alert>
            ))}
          </Stack>
        </Paper>
      </SimpleGrid>

      <SimpleGrid cols={{ base: 1, xl: 2 }} spacing="lg">
        <Paper p="lg" radius="2rem" className="surface-card strong">
          <Stack gap="md">
            <Group justify="space-between">
              <div>
                <Text size="sm" className="muted-text">
                  {t('pages.sizing.results.server.subtitle')}
                </Text>
                <Text fw={800}>{t('pages.sizing.results.server.title')}</Text>
              </div>
              <ThemeIcon size={42} radius="xl" variant="light" color="brand">
                <IconServer size={20} />
              </ThemeIcon>
            </Group>
            <ScrollArea>
              <ServerNodeTable rows={result.nodes} t={t} />
            </ScrollArea>
          </Stack>
        </Paper>

        <Paper p="lg" radius="2rem" className="surface-card strong">
          <Stack gap="md">
            <Group justify="space-between">
              <div>
                <Text size="sm" className="muted-text">
                  {t('pages.sizing.results.clients.subtitle')}
                </Text>
                <Text fw={800}>{t('pages.sizing.results.clients.title')}</Text>
              </div>
              <ThemeIcon size={42} radius="xl" variant="light" color="brand">
                <IconDeviceDesktop size={20} />
              </ThemeIcon>
            </Group>
            <ScrollArea>
              <Table highlightOnHover withTableBorder verticalSpacing="sm">
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t('pages.sizing.results.clients.columns.role')}</Table.Th>
                    <Table.Th ta="center">{t('pages.sizing.results.clients.columns.count')}</Table.Th>
                    <Table.Th>{t('pages.sizing.results.clients.columns.cpu')}</Table.Th>
                    <Table.Th ta="center">{t('pages.sizing.results.clients.columns.ram')}</Table.Th>
                    <Table.Th ta="center">{t('pages.sizing.results.clients.columns.storage')}</Table.Th>
                    <Table.Th>{t('pages.sizing.results.clients.columns.notes')}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {workstationSummary ? (
                    <Table.Tr>
                      <Table.Td>{t('pages.sizing.export.system.workstation')}</Table.Td>
                      <Table.Td ta="center">{workstationSummary.count}</Table.Td>
                      <Table.Td>{workstationSummary.cpuLabel}</Table.Td>
                      <Table.Td ta="center">{workstationSummary.ramGb} GB</Table.Td>
                      <Table.Td ta="center">{workstationSummary.storageGb} GB</Table.Td>
                      <Table.Td>
                        <Text size="sm" className="muted-text">
                          {t('pages.sizing.export.notes.clientSummary', {
                            count: workstationSummary.count
                          })}
                        </Text>
                      </Table.Td>
                    </Table.Tr>
                  ) : (
                    <Table.Tr>
                      <Table.Td>{t('pages.sizing.export.system.workstation')}</Table.Td>
                      <Table.Td ta="center">0</Table.Td>
                      <Table.Td>-</Table.Td>
                      <Table.Td ta="center">-</Table.Td>
                      <Table.Td ta="center">-</Table.Td>
                      <Table.Td>
                        <Text size="sm" className="muted-text">
                          {t('pages.sizing.export.notes.noClient')}
                        </Text>
                      </Table.Td>
                    </Table.Tr>
                  )}
                </Table.Tbody>
              </Table>
            </ScrollArea>
          </Stack>
        </Paper>
      </SimpleGrid>
    </Stack>
  );
}
