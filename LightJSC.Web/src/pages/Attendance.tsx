import {
  ActionIcon,
  Badge,
  Button,
  Center,
  Group,
  Image,
  Loader,
  Modal,
  MultiSelect,
  Paper,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Table,
  Tabs,
  Text
} from '@mantine/core';
import { MonthPickerInput } from '@mantine/dates';
import { notifications } from '@mantine/notifications';
import {
  IconCalendarStats,
  IconClock,
  IconDownload,
  IconEye,
  IconRefresh,
  IconSearch
} from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import {
  getAttendanceSummary,
  getFaceEvent,
  listCameras,
  listPersons,
  searchFaceEvents
} from '../api/ingestor';
import type {
  AttendanceSummaryDayCell,
  AttendanceSummaryPersonRow,
  CameraResponse,
  FaceEventRecord,
  PersonResponse
} from '../api/types';
import { useI18n } from '../i18n/I18nProvider';
import { formatDateTime } from '../utils/format';

const STICKY_WIDTHS = {
  index: 72,
  name: 240,
  personalId: 210,
  category: 180
} as const;

const STICKY_LEFTS = {
  index: 0,
  name: STICKY_WIDTHS.index,
  personalId: STICKY_WIDTHS.index + STICKY_WIDTHS.name,
  category: STICKY_WIDTHS.index + STICKY_WIDTHS.name + STICKY_WIDTHS.personalId
} as const;

const resolveImageSrc = (value?: string | null) => {
  if (!value) {
    return null;
  }

  return value.startsWith('data:image') ? value : `data:image/jpeg;base64,${value}`;
};

const formatPersonName = (person?: PersonResponse | null) => {
  if (!person) {
    return '-';
  }

  const parts = [person.firstName?.trim(), person.lastName?.trim()].filter(Boolean);
  const name = parts.join(' ').trim();
  return name || person.code || '-';
};

const getMonthRangeUtc = (year: number, month: number) => {
  const from = new Date(year, month - 1, 1, 0, 0, 0, 0);
  const to = new Date(year, month, 1, 0, 0, 0, 0);
  return { fromUtc: from.toISOString(), toUtc: to.toISOString() };
};

const getDayRangeUtc = (year: number, month: number, day: number) => {
  const from = new Date(year, month - 1, day, 0, 0, 0, 0);
  const to = new Date(year, month - 1, day + 1, 0, 0, 0, 0);
  return { fromUtc: from.toISOString(), toUtc: to.toISOString() };
};

const getDaysInMonth = (year: number, month: number) => {
  if (!Number.isFinite(year) || !Number.isFinite(month) || month < 1 || month > 12) {
    return 31;
  }

  return new Date(year, month, 0).getDate();
};

const buildEmptyDays = (daysInMonth: number): AttendanceSummaryDayCell[] =>
  Array.from({ length: daysInMonth }, (_, index) => ({
    day: index + 1,
    inEvent: null,
    outEvent: null
  }));

const formatTimeOnly = (value?: string | null, language: 'vi' | 'en' = 'vi') => {
  if (!value) {
    return '--';
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '--';
  }

  return new Intl.DateTimeFormat(language === 'vi' ? 'vi-VN' : 'en-GB', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false
  }).format(date);
};

const formatGender = (value?: string | null, language: 'vi' | 'en' = 'vi') => {
  if (!value) {
    return '-';
  }

  const normalized = value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .trim()
    .toLowerCase();

  if (normalized === 'male' || normalized === 'nam') {
    return language === 'vi' ? 'Nam' : 'Male';
  }

  if (normalized === 'female' || normalized === 'nu') {
    return language === 'vi' ? 'Nữ' : 'Female';
  }

  return value;
};

const buildStickyStyle = (left: number, width: number) => ({
  left,
  minWidth: width,
  width
});

function FragmentHeader({
  inLabel,
  outLabel,
  groupStart
}: {
  inLabel: string;
  outLabel: string;
  groupStart?: boolean;
}) {
  return (
    <>
      <Table.Th
        ta="center"
        className={`attendance-subhead-cell${groupStart ? ' attendance-day-group-start' : ''}`}
      >
        {inLabel}
      </Table.Th>
      <Table.Th ta="center" className="attendance-subhead-cell">
        {outLabel}
      </Table.Th>
    </>
  );
}

function DayCell({
  day,
  language,
  onOpenEvent,
  groupStart
}: {
  day: AttendanceSummaryDayCell;
  language: 'vi' | 'en';
  onOpenEvent: (eventId: string) => void;
  groupStart?: boolean;
}) {
  const renderValue = (eventId?: string | null, eventTimeUtc?: string | null) => {
    if (!eventId || !eventTimeUtc) {
      return (
        <Text size="sm" className="muted-text attendance-empty-value">
          --
        </Text>
      );
    }

    return (
      <Button
        variant="subtle"
        color="brand"
        size="compact-sm"
        className="attendance-time-button"
        onClick={() => onOpenEvent(eventId)}
      >
        {formatTimeOnly(eventTimeUtc, language)}
      </Button>
    );
  };

  return (
    <>
      <Table.Td
        ta="center"
        className={`attendance-time-cell${groupStart ? ' attendance-day-group-start' : ''}`}
      >
        {renderValue(day.inEvent?.eventId, day.inEvent?.eventTimeUtc)}
      </Table.Td>
      <Table.Td ta="center" className="attendance-time-cell">
        {renderValue(day.outEvent?.eventId, day.outEvent?.eventTimeUtc)}
      </Table.Td>
    </>
  );
}

function AttendanceEventCard({
  event,
  cameraLabel
}: {
  event: FaceEventRecord;
  cameraLabel?: string;
}) {
  const { t, language } = useI18n();
  const imageSrc = resolveImageSrc(event.bestshotBase64);

  return (
    <Paper p="md" radius="lg" className="surface-card strong">
      <Group align="flex-start" wrap="nowrap">
        {imageSrc ? (
          <Image src={imageSrc} w={92} h={92} radius="md" fit="cover" />
        ) : (
          <Center w={92} h={92} className="surface-card" style={{ borderRadius: 14 }}>
            <Text size="xs" className="muted-text">
              {t('common.empty.noImage')}
            </Text>
          </Center>
        )}

        <Stack gap={6} style={{ flex: 1 }}>
          <Group justify="space-between" wrap="wrap">
            <Text fw={700}>{cameraLabel ?? event.cameraId}</Text>
            <Badge variant="light" color="brand">
              {event.isKnown ? t('common.status.known') : t('common.status.unknown')}
            </Badge>
          </Group>

          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
            <Text size="sm">{t('common.fields.time')}: {formatDateTime(event.eventTimeUtc, t)}</Text>
            <Text size="sm">{t('common.fields.camera')}: {cameraLabel ?? event.cameraId}</Text>
            <Text size="sm">{t('common.fields.gender')}: {formatGender(event.gender, language)}</Text>
            <Text size="sm">{t('common.fields.age')}: {event.age ?? '-'}</Text>
            <Text size="sm">{t('common.fields.score')}: {event.score?.toFixed(3) ?? '-'}</Text>
            <Text size="sm">{t('common.fields.similarity')}: {event.similarity?.toFixed(3) ?? '-'}</Text>
            <Text size="sm">{t('pages.attendance.columns.department')}: {event.person?.category ?? '-'}</Text>
            <Text size="sm">{t('common.fields.remarks')}: {event.person?.remarks ?? '-'}</Text>
          </SimpleGrid>
        </Stack>
      </Group>
    </Paper>
  );
}

function DetailEventList({
  loading,
  events,
  cameraLabelById,
  emptyMessage,
  onOpenEvent
}: {
  loading: boolean;
  events: FaceEventRecord[];
  cameraLabelById: Map<string, string>;
  emptyMessage: string;
  onOpenEvent: (eventId: string) => void;
}) {
  const { t } = useI18n();

  if (loading) {
    return (
      <Center py="xl">
        <Loader color="brand" />
      </Center>
    );
  }

  if (events.length === 0) {
    return (
      <Center py="xl">
        <Stack gap="xs" align="center">
          <IconClock size={28} />
          <Text fw={600}>{t('pages.attendance.empty.title')}</Text>
          <Text size="sm" className="muted-text">
            {emptyMessage}
          </Text>
        </Stack>
      </Center>
    );
  }

  return (
    <Stack gap="sm" className="attendance-detail-list">
      {events.map((event) => (
        <Stack key={event.id} gap="xs">
          <AttendanceEventCard
            event={event}
            cameraLabel={cameraLabelById.get(event.cameraId)}
          />
          <Group justify="flex-end">
            <Button
              variant="light"
              color="brand"
              leftSection={<IconEye size={14} />}
              onClick={() => onOpenEvent(event.id)}
            >
              {t('pages.attendance.actions.viewDetail')}
            </Button>
          </Group>
        </Stack>
      ))}
    </Stack>
  );
}

export function Attendance() {
  const { t, language } = useI18n();
  const now = new Date();
  const [selectedPeriod, setSelectedPeriod] = useState<Date | null>(
    new Date(now.getFullYear(), now.getMonth(), 1)
  );
  const [selectedCameraIds, setSelectedCameraIds] = useState<string[]>([]);
  const [selectedCategories, setSelectedCategories] = useState<string[]>([]);
  const [selectedPersonIds, setSelectedPersonIds] = useState<string[]>([]);
  const [eventDetailId, setEventDetailId] = useState<string | null>(null);
  const [personDetail, setPersonDetail] = useState<AttendanceSummaryPersonRow | null>(null);
  const [detailDay, setDetailDay] = useState<string>('all');

  const personsQuery = useQuery({
    queryKey: ['persons', 'attendance'],
    queryFn: listPersons
  });

  const camerasQuery = useQuery({
    queryKey: ['cameras', 'attendance'],
    queryFn: listCameras
  });

  const month = selectedPeriod ? selectedPeriod.getMonth() + 1 : now.getMonth() + 1;
  const year = selectedPeriod ? selectedPeriod.getFullYear() : now.getFullYear();

  const attendanceQuery = useQuery({
    queryKey: ['attendance', year, month, selectedCameraIds, selectedCategories, selectedPersonIds],
    queryFn: () =>
      getAttendanceSummary({
        year,
        month,
        cameraIds: selectedCameraIds.length > 0 ? selectedCameraIds : undefined,
        categories: selectedCategories.length > 0 ? selectedCategories : undefined,
        personIds: selectedPersonIds.length > 0 ? selectedPersonIds : undefined
      }),
    enabled: Number.isFinite(year) && Number.isFinite(month)
  });

  const eventDetailQuery = useQuery({
    queryKey: ['attendance', 'event', eventDetailId],
    queryFn: () => getFaceEvent(eventDetailId as string, true),
    enabled: Boolean(eventDetailId)
  });

  const monthEventRange = useMemo(() => getMonthRangeUtc(year, month), [month, year]);

  const monthPersonEventsQuery = useQuery({
    queryKey: ['attendance', 'person-detail', personDetail?.personId, monthEventRange, selectedCameraIds],
    queryFn: () =>
      searchFaceEvents({
        fromUtc: monthEventRange.fromUtc,
        toUtc: monthEventRange.toUtc,
        personIds: personDetail ? [personDetail.personId] : undefined,
        cameraIds: selectedCameraIds.length > 0 ? selectedCameraIds : undefined,
        isKnown: true,
        includeBestshot: true,
        page: 1,
        pageSize: 5000
      }),
    enabled: Boolean(personDetail?.personId)
  });

  const dayEventRange = useMemo(() => {
    if (detailDay === 'all') {
      return null;
    }

    return getDayRangeUtc(year, month, Number(detailDay));
  }, [detailDay, month, year]);

  const dayPersonEventsQuery = useQuery({
    queryKey: ['attendance', 'person-detail-day', personDetail?.personId, dayEventRange, selectedCameraIds],
    queryFn: () =>
      searchFaceEvents({
        fromUtc: dayEventRange?.fromUtc,
        toUtc: dayEventRange?.toUtc,
        personIds: personDetail ? [personDetail.personId] : undefined,
        cameraIds: selectedCameraIds.length > 0 ? selectedCameraIds : undefined,
        isKnown: true,
        includeBestshot: true,
        page: 1,
        pageSize: 5000
      }),
    enabled: Boolean(personDetail?.personId && dayEventRange)
  });

  const cameraOptions = useMemo(
    () =>
      (camerasQuery.data ?? [])
        .filter((camera: CameraResponse) => camera.enabled)
        .map((camera: CameraResponse) => ({
          value: camera.cameraId,
          label: `${camera.code?.trim() || camera.cameraId}${camera.ipAddress ? ` - ${camera.ipAddress}` : ''}`
        })),
    [camerasQuery.data]
  );

  const cameraLabelById = useMemo(
    () =>
      new Map(
        (camerasQuery.data ?? []).map((camera) => [
          camera.cameraId,
          `${camera.code?.trim() || camera.cameraId}${camera.ipAddress ? ` - ${camera.ipAddress}` : ''}`
        ])
      ),
    [camerasQuery.data]
  );

  const categoryOptions = useMemo(
    () =>
      Array.from(
        new Set((personsQuery.data ?? []).map((person) => person.category?.trim()).filter(Boolean) as string[])
      )
        .sort((left, right) => left.localeCompare(right, language === 'vi' ? 'vi' : 'en'))
        .map((category) => ({ value: category, label: category })),
    [language, personsQuery.data]
  );

  const personOptions = useMemo(() => {
    const categorySet = new Set(selectedCategories);
    return (personsQuery.data ?? [])
      .filter((person) => (categorySet.size === 0 ? true : categorySet.has(person.category ?? '')))
      .map((person) => ({
        value: person.id,
        label: `${formatPersonName(person)}${person.personalId ? ` - ${person.personalId}` : ''}`
      }))
      .sort((left, right) => left.label.localeCompare(right.label, language === 'vi' ? 'vi' : 'en'));
  }, [language, personsQuery.data, selectedCategories]);

  useEffect(() => {
    setSelectedPersonIds((prev) => prev.filter((item) => personOptions.some((option) => option.value === item)));
  }, [personOptions]);

  useEffect(() => {
    if (!personDetail) {
      setDetailDay('all');
      return;
    }

    const firstActiveDay = personDetail.days.find((day) => day.inEvent || day.outEvent)?.day;
    setDetailDay(firstActiveDay ? String(firstActiveDay) : 'all');
  }, [personDetail]);

  const daysInMonth = useMemo(
    () => attendanceQuery.data?.daysInMonth ?? getDaysInMonth(year, month),
    [attendanceQuery.data?.daysInMonth, month, year]
  );

  const dayOptions = useMemo(
    () => [
      { value: 'all', label: t('pages.attendance.detail.allDays') },
      ...Array.from({ length: daysInMonth }, (_, index) => ({
        value: String(index + 1),
        label: t('pages.attendance.detail.dayLabel', { day: index + 1 })
      }))
    ],
    [daysInMonth, t]
  );

  const tableRows = useMemo(() => {
    const summaryRows = attendanceQuery.data?.items ?? [];
    const summaryMap = new Map(summaryRows.map((row) => [row.personId, row]));
    const categorySet = new Set(selectedCategories);
    const personSet = new Set(selectedPersonIds);
    const filteredPersons = (personsQuery.data ?? []).filter((person) => {
      if (categorySet.size > 0 && !categorySet.has(person.category ?? '')) {
        return false;
      }

      if (personSet.size > 0 && !personSet.has(person.id)) {
        return false;
      }

      return true;
    });

    const rows: AttendanceSummaryPersonRow[] = filteredPersons.map((person) => {
      const existing = summaryMap.get(person.id);
      if (existing) {
        return existing;
      }

      return {
        personId: person.id,
        fullName: formatPersonName(person),
        personalId: person.personalId ?? person.code,
        category: person.category,
        days: buildEmptyDays(daysInMonth)
      };
    });

    const seen = new Set(rows.map((row) => row.personId));
    for (const row of summaryRows) {
      if (!seen.has(row.personId)) {
        rows.push(row);
      }
    }

    return rows.sort((left, right) =>
      left.fullName.localeCompare(right.fullName, language === 'vi' ? 'vi' : 'en', {
        sensitivity: 'base'
      })
    );
  }, [
    attendanceQuery.data?.items,
    daysInMonth,
    language,
    personsQuery.data,
    selectedCategories,
    selectedPersonIds
  ]);

  const refreshAll = () => {
    void Promise.all([attendanceQuery.refetch(), personsQuery.refetch(), camerasQuery.refetch()]);
  };

  const exportCsv = () => {
    if (tableRows.length === 0) {
      notifications.show({
        color: 'yellow',
        title: t('pages.attendance.export.emptyTitle'),
        message: t('pages.attendance.export.emptyMessage')
      });
      return;
    }

    const headers = [
      t('pages.attendance.columns.index'),
      t('pages.attendance.columns.fullName'),
      t('pages.attendance.columns.personalId'),
      t('pages.attendance.columns.department'),
      ...Array.from({ length: daysInMonth }, (_, index) => [
        `${t('pages.attendance.export.dayPrefix')} ${String(index + 1).padStart(2, '0')} ${t('pages.attendance.columns.in')}`,
        `${t('pages.attendance.export.dayPrefix')} ${String(index + 1).padStart(2, '0')} ${t('pages.attendance.columns.out')}`
      ]).flat()
    ];

    const escapeCell = (value: string) => `"${value.replaceAll('"', '""')}"`;
    const rows = tableRows.map((row, index) => {
      const dayCells = row.days.flatMap((day) => [
        formatTimeOnly(day.inEvent?.eventTimeUtc, language),
        formatTimeOnly(day.outEvent?.eventTimeUtc, language)
      ]);

      return [
        String(index + 1),
        row.fullName,
        row.personalId ?? '',
        row.category ?? '',
        ...dayCells
      ]
        .map(escapeCell)
        .join(',');
    });

    const csv = ['\uFEFF' + headers.map(escapeCell).join(','), ...rows].join('\r\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `attendance_${year}_${String(month).padStart(2, '0')}.csv`;
    anchor.click();
    URL.revokeObjectURL(url);
  };

  const dayTabEvents = detailDay === 'all'
    ? (monthPersonEventsQuery.data?.items ?? [])
    : (dayPersonEventsQuery.data?.items ?? []);
  const dayTabLoading = detailDay === 'all'
    ? monthPersonEventsQuery.isLoading
    : dayPersonEventsQuery.isLoading;

  return (
    <>
      <Stack gap="lg" className="page">
        <Group justify="space-between" align="flex-start" wrap="wrap">
          <Stack gap={4}>
            <Text size="sm" className="muted-text">
              {t('pages.attendance.subtitle')}
            </Text>
            <Text size="xl" fw={700}>
              {t('pages.attendance.title')}
            </Text>
          </Stack>

          <Group gap="sm" wrap="wrap">
            <Button
              variant="light"
              color="brand"
              leftSection={<IconDownload size={16} />}
              onClick={exportCsv}
            >
              {t('pages.attendance.actions.export')}
            </Button>
            <ActionIcon
              size="lg"
              variant="light"
              color="brand"
              onClick={refreshAll}
              aria-label={t('pages.attendance.actions.refresh')}
            >
              <IconRefresh size={18} />
            </ActionIcon>
          </Group>
        </Group>

        <Paper p="lg" radius="xl" className="surface-card strong">
          <Stack gap="md">
            <Group gap="xs">
              <IconSearch size={16} />
              <Text fw={600}>{t('pages.attendance.filters.title')}</Text>
            </Group>

            <div className="attendance-filter-grid">
              <MonthPickerInput
                label={t('pages.attendance.filters.period', undefined, language === 'vi' ? 'Tháng / năm' : 'Month / year')}
                locale={language === 'vi' ? 'vi' : 'en'}
                valueFormat="MMMM YYYY"
                value={selectedPeriod}
                onChange={(value) => setSelectedPeriod(value ? new Date(value) : null)}
                clearable={false}
                placeholder={t(
                  'pages.attendance.filters.periodPlaceholder',
                  undefined,
                  language === 'vi' ? 'Chọn tháng / năm' : 'Select month / year'
                )}
              />
              <MultiSelect
                label={t('pages.attendance.filters.cameras')}
                data={cameraOptions}
                value={selectedCameraIds}
                onChange={setSelectedCameraIds}
                placeholder={t('pages.attendance.filters.camerasPlaceholder')}
                searchable
                clearable
              />
              <MultiSelect
                label={t('pages.attendance.filters.departments')}
                data={categoryOptions}
                value={selectedCategories}
                onChange={setSelectedCategories}
                placeholder={t('pages.attendance.filters.departmentsPlaceholder')}
                searchable
                clearable
              />
              <MultiSelect
                label={t('pages.attendance.filters.people')}
                data={personOptions}
                value={selectedPersonIds}
                onChange={setSelectedPersonIds}
                placeholder={t('pages.attendance.filters.peoplePlaceholder')}
                searchable
                clearable
              />
            </div>
          </Stack>
        </Paper>

        <Paper p="lg" radius="xl" className="surface-card strong">
          <Stack gap="md">
            <Group justify="space-between" wrap="wrap">
              <Text fw={700}>{t('pages.attendance.tableTitle')}</Text>
              <Badge variant="light" color="brand">
                {t('pages.attendance.totalRows', { count: tableRows.length })}
              </Badge>
            </Group>

            {attendanceQuery.isLoading || personsQuery.isLoading ? (
              <Center py="xl">
                <Stack gap="sm" align="center">
                  <Loader color="brand" />
                  <Text>{t('pages.attendance.states.loading')}</Text>
                </Stack>
              </Center>
            ) : (
              <ScrollArea className="attendance-table-scroll" offsetScrollbars>
                <Table withTableBorder highlightOnHover className="attendance-table" stickyHeader>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th
                        rowSpan={2}
                        className="attendance-sticky-col attendance-sticky-index"
                        style={buildStickyStyle(STICKY_LEFTS.index, STICKY_WIDTHS.index)}
                      >
                        {t('pages.attendance.columns.index')}
                      </Table.Th>
                      <Table.Th
                        rowSpan={2}
                        className="attendance-sticky-col attendance-sticky-name"
                        style={buildStickyStyle(STICKY_LEFTS.name, STICKY_WIDTHS.name)}
                      >
                        {t('pages.attendance.columns.fullName')}
                      </Table.Th>
                      <Table.Th
                        rowSpan={2}
                        className="attendance-sticky-col attendance-sticky-personalId"
                        style={buildStickyStyle(STICKY_LEFTS.personalId, STICKY_WIDTHS.personalId)}
                      >
                        {t('pages.attendance.columns.personalId')}
                      </Table.Th>
                      <Table.Th
                        rowSpan={2}
                        className="attendance-sticky-col attendance-sticky-category"
                        style={buildStickyStyle(STICKY_LEFTS.category, STICKY_WIDTHS.category)}
                      >
                        {t('pages.attendance.columns.department')}
                      </Table.Th>
                      {Array.from({ length: daysInMonth }, (_, index) => (
                        <Table.Th
                          key={`head-${index + 1}`}
                          colSpan={2}
                          ta="center"
                          className="attendance-day-group-head"
                        >
                          {t('pages.attendance.columns.dayLabel', { day: index + 1 })}
                        </Table.Th>
                      ))}
                    </Table.Tr>
                    <Table.Tr>
                      {Array.from({ length: daysInMonth }, (_, index) => (
                        <FragmentHeader
                          key={`sub-${index + 1}`}
                          inLabel={t('pages.attendance.columns.in')}
                          outLabel={t('pages.attendance.columns.out')}
                          groupStart
                        />
                      ))}
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {tableRows.length === 0 ? (
                      <Table.Tr>
                        <Table.Td colSpan={4 + daysInMonth * 2}>
                          <Center py="xl">
                            <Stack gap="xs" align="center">
                              <IconCalendarStats size={28} />
                              <Text fw={600}>{t('pages.attendance.empty.title')}</Text>
                              <Text size="sm" className="muted-text">
                                {t('pages.attendance.empty.message')}
                              </Text>
                            </Stack>
                          </Center>
                        </Table.Td>
                      </Table.Tr>
                    ) : (
                      tableRows.map((row, index) => (
                        <Table.Tr key={row.personId}>
                          <Table.Td
                            className="attendance-sticky-col attendance-sticky-index"
                            style={buildStickyStyle(STICKY_LEFTS.index, STICKY_WIDTHS.index)}
                          >
                            {index + 1}
                          </Table.Td>
                          <Table.Td
                            className="attendance-sticky-col attendance-sticky-name"
                            style={buildStickyStyle(STICKY_LEFTS.name, STICKY_WIDTHS.name)}
                          >
                            <Button
                              variant="subtle"
                              color="brand"
                              size="compact-sm"
                              className="attendance-person-button"
                              onClick={() => setPersonDetail(row)}
                            >
                              {row.fullName}
                            </Button>
                          </Table.Td>
                          <Table.Td
                            className="attendance-sticky-col attendance-sticky-personalId"
                            style={buildStickyStyle(STICKY_LEFTS.personalId, STICKY_WIDTHS.personalId)}
                          >
                            {row.personalId ?? '-'}
                          </Table.Td>
                          <Table.Td
                            className="attendance-sticky-col attendance-sticky-category"
                            style={buildStickyStyle(STICKY_LEFTS.category, STICKY_WIDTHS.category)}
                          >
                            {row.category ?? '-'}
                          </Table.Td>
                          {row.days.map((day) => (
                            <DayCell
                              key={`${row.personId}-${day.day}`}
                              day={day}
                              language={language}
                              onOpenEvent={setEventDetailId}
                              groupStart
                            />
                          ))}
                        </Table.Tr>
                      ))
                    )}
                  </Table.Tbody>
                </Table>
              </ScrollArea>
            )}
          </Stack>
        </Paper>
      </Stack>

      <Modal
        opened={Boolean(eventDetailId)}
        onClose={() => setEventDetailId(null)}
        title={t('pages.attendance.detail.eventTitle')}
        size="lg"
      >
        {eventDetailQuery.isLoading ? (
          <Center py="xl">
            <Loader color="brand" />
          </Center>
        ) : eventDetailQuery.data ? (
          <AttendanceEventCard
            event={eventDetailQuery.data}
            cameraLabel={cameraLabelById.get(eventDetailQuery.data.cameraId)}
          />
        ) : (
          <Text>{t('pages.attendance.detail.noEvent')}</Text>
        )}
      </Modal>

      <Modal
        opened={Boolean(personDetail)}
        onClose={() => setPersonDetail(null)}
        title={personDetail?.fullName ?? t('pages.attendance.detail.personTitle')}
        size="80rem"
      >
        <Stack gap="md">
          <Group justify="space-between" wrap="wrap">
            <Badge variant="light" color="brand">
              {personDetail?.personalId ?? '-'}
            </Badge>
            <Select
              data={dayOptions}
              value={detailDay}
              onChange={(value) => setDetailDay(value ?? 'all')}
              label={t('pages.attendance.detail.dayFilter')}
              w={220}
            />
          </Group>

          <Tabs defaultValue="month">
            <Tabs.List>
              <Tabs.Tab value="month">{t('pages.attendance.detail.monthTab')}</Tabs.Tab>
              <Tabs.Tab value="day">{t('pages.attendance.detail.dayTab')}</Tabs.Tab>
            </Tabs.List>

            <Tabs.Panel value="month" pt="md">
              <DetailEventList
                loading={monthPersonEventsQuery.isLoading}
                events={monthPersonEventsQuery.data?.items ?? []}
                cameraLabelById={cameraLabelById}
                emptyMessage={t('pages.attendance.detail.monthEmpty')}
                onOpenEvent={setEventDetailId}
              />
            </Tabs.Panel>

            <Tabs.Panel value="day" pt="md">
              <DetailEventList
                loading={dayTabLoading}
                events={dayTabEvents}
                cameraLabelById={cameraLabelById}
                emptyMessage={
                  detailDay === 'all'
                    ? t('pages.attendance.detail.selectDayHint')
                    : t('pages.attendance.detail.dayEmpty')
                }
                onOpenEvent={setEventDetailId}
              />
            </Tabs.Panel>
          </Tabs>
        </Stack>
      </Modal>
    </>
  );
}
