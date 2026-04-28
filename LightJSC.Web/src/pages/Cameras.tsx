import {
  ActionIcon,
  Badge,
  Button,
  Checkbox,
  Divider,
  Group,
  Modal,
  Pagination,
  Paper,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Table,
  Text,
  TextInput,
  Tooltip,
  Switch
} from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import {
  IconCheck,
  IconEdit,
  IconPlayerPlay,
  IconPlus,
  IconSearch,
  IconTrash,
  IconX
} from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import { useForm } from '@mantine/form';
import {
  createCamera,
  discoverCameras,
  deleteCamera,
  listCameras,
  testCameraRtsp,
  updateCamera
} from '../api/ingestor';
import type { CameraRequest, CameraResponse, DiscoveredCameraResponse } from '../api/types';
import { useI18n } from '../i18n/I18nProvider';
import { formatDateTime } from '../utils/format';

const defaultValues: CameraRequest = {
  cameraId: '',
  code: '',
  ipAddress: '',
  rtspUsername: '',
  rtspPassword: '',
  rtspProfile: 'def_profile1',
  rtspPath: '/ONVIF/MediaInput',
  cameraSeries: '',
  cameraModel: '',
  enabled: true
};

type BulkCameraDefaults = {
  rtspUsername: string;
  rtspPassword: string;
  rtspProfile: string;
  rtspPath: string;
  enabled: boolean;
};

const defaultBulkValues: BulkCameraDefaults = {
  rtspUsername: '',
  rtspPassword: '',
  rtspProfile: defaultValues.rtspProfile,
  rtspPath: defaultValues.rtspPath,
  enabled: true
};

const normalizeHost = (value?: string | null) => {
  if (!value) {
    return '';
  }

  const trimmed = value.trim();
  if (!trimmed) {
    return '';
  }

  return trimmed.split(':', 2)[0].toLowerCase();
};

const parseIpv4 = (value: string) => {
  const host = normalizeHost(value);
  if (!host) {
    return null;
  }

  const parts = host.split('.');
  if (parts.length !== 4) {
    return null;
  }

  let total = 0;
  for (const part of parts) {
    if (!/^\d+$/.test(part)) {
      return null;
    }
    const octet = Number(part);
    if (!Number.isInteger(octet) || octet < 0 || octet > 255) {
      return null;
    }
    total = total * 256 + octet;
  }

  return total;
};

const getDiscoveredKey = (camera: DiscoveredCameraResponse, index: number) =>
  camera.deviceId ?? camera.ipAddress ?? camera.xAddr ?? `scan-${index}`;

export function Cameras() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<CameraResponse | null>(null);
  const [discovered, setDiscovered] = useState<DiscoveredCameraResponse[]>([]);
  const [opened, { open, close }] = useDisclosure(false);
  const [scanOpened, { open: openScan, close: closeScan }] = useDisclosure(false);
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState('20');

  const camerasQuery = useQuery({
    queryKey: ['cameras'],
    queryFn: listCameras
  });

  const scanForm = useForm({
    initialValues: {
      ipStart: '',
      ipEnd: ''
    },
    validate: (values) => {
      const errors: Record<string, string> = {};
      const start = values.ipStart.trim();
      const end = values.ipEnd.trim();
      const startValue = start ? parseIpv4(start) : null;
      const endValue = end ? parseIpv4(end) : null;

      if ((start && !end) || (!start && end)) {
        errors.ipStart = t('pages.cameras.scan.validation.rangeRequired');
        errors.ipEnd = t('pages.cameras.scan.validation.rangeRequired');
        return errors;
      }

      if (start && startValue === null) {
        errors.ipStart = t('pages.cameras.scan.validation.invalidIp');
      }

      if (end && endValue === null) {
        errors.ipEnd = t('pages.cameras.scan.validation.invalidIp');
      }

      if (startValue !== null && endValue !== null && startValue > endValue) {
        errors.ipEnd = t('pages.cameras.scan.validation.endBeforeStart');
      }

      return errors;
    }
  });

  const bulkForm = useForm<BulkCameraDefaults>({
    initialValues: defaultBulkValues
  });

  const form = useForm<CameraRequest>({
    initialValues: defaultValues,
    validate: {
      cameraId: (value) =>
        value.trim().length === 0 ? t('pages.cameras.validation.cameraIdRequired') : null,
      ipAddress: (value) =>
        value.trim().length === 0 ? t('pages.cameras.validation.ipRequired') : null,
      rtspProfile: (value) =>
        value.trim().length === 0 ? t('pages.cameras.validation.profileRequired') : null,
      rtspPath: (value) =>
        value.trim().length === 0 ? t('pages.cameras.validation.pathRequired') : null
    }
  });

  const existingHosts = useMemo(() => {
    return new Set(
      (camerasQuery.data ?? [])
        .map((camera) => normalizeHost(camera.ipAddress))
        .filter((value) => value.length > 0)
    );
  }, [camerasQuery.data]);

  const filteredCameras = useMemo(() => {
    const term = search.trim().toLowerCase();
    if (!term) {
      return camerasQuery.data ?? [];
    }

    return (camerasQuery.data ?? []).filter((camera) => {
      return (
        camera.cameraId.toLowerCase().includes(term) ||
        (camera.code ?? '').toLowerCase().includes(term) ||
        camera.ipAddress.toLowerCase().includes(term) ||
        (camera.cameraSeries ?? '').toLowerCase().includes(term) ||
        (camera.cameraModel ?? '').toLowerCase().includes(term)
      );
    });
  }, [camerasQuery.data, search]);

  const pageSizeValue = Number(pageSize);
  const totalCount = filteredCameras.length;
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSizeValue));
  const fromIndex = totalCount ? (page - 1) * pageSizeValue + 1 : 0;
  const toIndex = Math.min(page * pageSizeValue, totalCount);
  const pagedCameras = useMemo(() => {
    const start = (page - 1) * pageSizeValue;
    return filteredCameras.slice(start, start + pageSizeValue);
  }, [filteredCameras, page, pageSizeValue]);

  useEffect(() => {
    setPage(1);
  }, [pageSizeValue, search]);

  useEffect(() => {
    if (page > totalPages) {
      setPage(totalPages);
    }
  }, [page, totalPages]);

  const existingIds = useMemo(() => {
    return new Set((camerasQuery.data ?? []).map((camera) => camera.cameraId.toLowerCase()));
  }, [camerasQuery.data]);

  const discoveredItems = useMemo(() => {
    return discovered.map((camera, index) => {
      const key = getDiscoveredKey(camera, index);
      const host = normalizeHost(camera.ipAddress);
      const deviceId = camera.deviceId?.toLowerCase() ?? '';
      const isAdded = (host && existingHosts.has(host)) || (deviceId && existingIds.has(deviceId));
      const isSelectable = !isAdded && Boolean(host);
      return { camera, key, isAdded, isSelectable };
    });
  }, [discovered, existingHosts, existingIds]);

  const selectableKeys = useMemo(
    () => discoveredItems.filter((item) => item.isSelectable).map((item) => item.key),
    [discoveredItems]
  );

  const selectedCount = discoveredItems.filter((item) => selectedKeys.has(item.key)).length;
  const readyCount = discoveredItems.filter((item) => item.isSelectable).length;
  const alreadyAddedCount = discoveredItems.filter((item) => item.isAdded).length;

  const selectedItems = useMemo(
    () =>
      discoveredItems
        .filter((item) => item.isSelectable && selectedKeys.has(item.key))
        .map((item) => item.camera),
    [discoveredItems, selectedKeys]
  );

  const allSelected = selectableKeys.length > 0 && selectableKeys.every((key) => selectedKeys.has(key));
  const someSelected = selectableKeys.some((key) => selectedKeys.has(key));

  const saveMutation = useMutation({
    mutationFn: (payload: CameraRequest) =>
      editing ? updateCamera(editing.cameraId, payload) : createCamera(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['cameras'] });
      notifications.show({
        title: t('pages.cameras.notifications.save.title'),
        message: t('pages.cameras.notifications.save.message'),
        color: 'brand'
      });
      close();
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.cameras.notifications.saveFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const deleteMutation = useMutation({
    mutationFn: deleteCamera,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['cameras'] });
      notifications.show({
        title: t('pages.cameras.notifications.delete.title'),
        message: t('pages.cameras.notifications.delete.message'),
        color: 'brand'
      });
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.cameras.notifications.deleteFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const testMutation = useMutation({
    mutationFn: async (cameraId: string) => {
      const result = await testCameraRtsp(cameraId);
      return { cameraId, result };
    },
    onSuccess: ({ cameraId, result }) => {
      notifications.show({
        title: result.success
          ? t('pages.cameras.notifications.rtsp.success.title')
          : t('pages.cameras.notifications.rtsp.failure.title'),
        message: result.success
          ? t('pages.cameras.notifications.rtsp.success.message', { id: cameraId })
          : t('pages.cameras.notifications.rtsp.failure.message', { id: cameraId }),
        color: result.success ? 'brand' : 'amber'
      });
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.cameras.notifications.rtspError.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const scanMutation = useMutation({
    mutationFn: (range: { ipStart?: string; ipEnd?: string }) =>
      discoverCameras({
        timeoutSeconds: 4,
        ipStart: range.ipStart,
        ipEnd: range.ipEnd
      }),
    onSuccess: (items) => {
      setDiscovered(items);
      setSelectedKeys(new Set());
      if (items.length === 0) {
        notifications.show({
          title: t('pages.cameras.notifications.scan.empty.title'),
          message: t('pages.cameras.notifications.scan.empty.message'),
          color: 'yellow'
        });
      }
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.cameras.notifications.scanFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const bulkAddMutation = useMutation({
    mutationFn: async (payloads: CameraRequest[]) => {
      const results = await Promise.allSettled(
        payloads.map((payload) => createCamera(payload))
      );
      const successes = results.filter((result) => result.status === 'fulfilled').length;
      const failures = results.length - successes;
      return { total: results.length, successes, failures };
    },
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ['cameras'] });
      notifications.show({
        title:
          result.failures === 0
            ? t('pages.cameras.notifications.bulk.success.title')
            : t('pages.cameras.notifications.bulk.partial.title'),
        message:
          result.failures === 0
            ? t('pages.cameras.notifications.bulk.success.message', {
                success: result.successes
              })
            : t('pages.cameras.notifications.bulk.partial.message', {
                success: result.successes,
                failed: result.failures
              }),
        color: result.failures === 0 ? 'brand' : 'yellow'
      });
      setSelectedKeys(new Set());
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.cameras.notifications.bulkFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const openCreate = () => {
    setEditing(null);
    form.setValues(defaultValues);
    open();
  };

  const openEdit = (camera: CameraResponse) => {
    setEditing(camera);
    form.setValues({
      cameraId: camera.cameraId,
      code: camera.code ?? '',
      ipAddress: camera.ipAddress,
      rtspUsername: camera.rtspUsername,
      rtspPassword: '',
      rtspProfile: camera.rtspProfile,
      rtspPath: camera.rtspPath,
      cameraSeries: camera.cameraSeries ?? '',
      cameraModel: camera.cameraModel ?? '',
      enabled: camera.enabled
    });
    open();
  };

  const confirmDelete = (camera: CameraResponse) => {
    modals.openConfirmModal({
      title: t('pages.cameras.modals.delete.title'),
      children: (
        <Text size="sm">
          {t('pages.cameras.modals.delete.message', { id: camera.cameraId })}
        </Text>
      ),
      labels: { confirm: t('common.actions.delete'), cancel: t('common.actions.cancel') },
      confirmProps: { color: 'red' },
      onConfirm: () => deleteMutation.mutate(camera.cameraId)
    });
  };

  const suggestCameraId = (camera: DiscoveredCameraResponse) => {
    const fromDevice = camera.deviceId
      ?.split(':')
      .pop()
      ?.replace(/[{}]/g, '');
    const fromMac = camera.macAddress?.replace(/[^0-9A-Fa-f]/g, '').toUpperCase();
    const fromIp = camera.ipAddress?.replace(/[^0-9]/g, '');
    return (
      fromDevice ||
      fromMac ||
      (fromIp ? `CAM${fromIp}` : `CAM${Math.random().toString(16).slice(2, 8)}`)
    );
  };

  const applyDiscovered = (camera: DiscoveredCameraResponse) => {
    setEditing(null);
    form.setValues({
      ...defaultValues,
      cameraId: suggestCameraId(camera),
      code: '',
      ipAddress: camera.ipAddress ?? '',
      rtspUsername: '',
      rtspPassword: '',
      rtspProfile: defaultValues.rtspProfile,
      rtspPath: defaultValues.rtspPath,
      cameraSeries: camera.cameraSeries ?? '',
      cameraModel: camera.model ?? '',
      enabled: true
    });
    closeScan();
    open();
  };

  const buildUniqueCameraId = (camera: DiscoveredCameraResponse, usedIds: Set<string>) => {
    const base = suggestCameraId(camera);
    let candidate = base;
    let suffix = 2;
    while (usedIds.has(candidate.toLowerCase())) {
      candidate = `${base}-${suffix}`;
      suffix += 1;
    }
    usedIds.add(candidate.toLowerCase());
    return candidate;
  };

  const toggleSelection = (key: string, checked: boolean) => {
    setSelectedKeys((prev) => {
      const next = new Set(prev);
      if (checked) {
        next.add(key);
      } else {
        next.delete(key);
      }
      return next;
    });
  };

  const toggleSelectAll = (checked: boolean) => {
    if (!checked) {
      setSelectedKeys(new Set());
      return;
    }

    setSelectedKeys(new Set(selectableKeys));
  };

  const handleScan = () => {
    const validation = scanForm.validate();
    if (validation.hasErrors) {
      return;
    }

    const ipStart = scanForm.values.ipStart.trim();
    const ipEnd = scanForm.values.ipEnd.trim();
    scanMutation.mutate({
      ipStart: ipStart || undefined,
      ipEnd: ipEnd || undefined
    });
  };

  const handleBulkAdd = () => {
    if (selectedItems.length === 0) {
      notifications.show({
        title: t('pages.cameras.notifications.bulk.noneSelected.title'),
        message: t('pages.cameras.notifications.bulk.noneSelected.message'),
        color: 'yellow'
      });
      return;
    }

    const usedIds = new Set(existingIds);
    const payloads: CameraRequest[] = [];
    for (const camera of selectedItems) {
      const ipAddress = camera.ipAddress?.trim();
      if (!ipAddress) {
        continue;
      }

      const cameraId = buildUniqueCameraId(camera, usedIds);
      const discoveredSeries = camera.cameraSeries?.trim();
      const discoveredModel = camera.model?.trim();
      payloads.push({
        cameraId,
        ipAddress,
        rtspUsername: bulkForm.values.rtspUsername,
        rtspPassword: bulkForm.values.rtspPassword?.trim()
          ? bulkForm.values.rtspPassword
          : undefined,
        rtspProfile: bulkForm.values.rtspProfile,
        rtspPath: bulkForm.values.rtspPath,
        cameraSeries: discoveredSeries || undefined,
        cameraModel: discoveredModel || undefined,
        enabled: bulkForm.values.enabled
      });
    }

    if (payloads.length === 0) {
      notifications.show({
        title: t('pages.cameras.notifications.bulk.invalid.title'),
        message: t('pages.cameras.notifications.bulk.invalid.message'),
        color: 'yellow'
      });
      return;
    }

    bulkAddMutation.mutate(payloads);
  };

  const onSubmit = (values: CameraRequest) => {
    const trimmedCode = values.code?.trim() ?? '';
    const payload = {
      ...values,
      code: trimmedCode,
      rtspPassword: values.rtspPassword?.trim() ? values.rtspPassword : undefined,
      cameraSeries: values.cameraSeries?.trim() || undefined,
      cameraModel: values.cameraModel?.trim() || undefined
    };
    saveMutation.mutate(payload);
  };

  return (
    <Stack gap="lg" className="page">
      <Group justify="space-between" align="center">
        <Stack gap={4}>
          <Text size="sm" className="muted-text">
            {t('pages.cameras.subtitle')}
          </Text>
          <Text size="xl" fw={600}>
            {t('pages.cameras.title')}
          </Text>
        </Stack>
        <Group>
          <TextInput
            placeholder={t('pages.cameras.search.placeholder')}
            value={search}
            onChange={(event) => setSearch(event.currentTarget.value)}
            leftSection={<IconSearch size={16} />}
          />
          <Button
            variant="light"
            leftSection={<IconSearch size={18} />}
            onClick={openScan}
          >
            {t('pages.cameras.actions.scan')}
          </Button>
          <Button leftSection={<IconPlus size={18} />} onClick={openCreate}>
            {t('pages.cameras.actions.add')}
          </Button>
        </Group>
      </Group>

      <Paper p="lg" radius="lg" className="surface-card">
        <ScrollArea>
          <Table highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('pages.cameras.table.id')}</Table.Th>
                <Table.Th>{t('pages.cameras.table.code')}</Table.Th>
                <Table.Th>{t('pages.cameras.table.ip')}</Table.Th>
                <Table.Th>{t('pages.cameras.table.series')}</Table.Th>
                <Table.Th>{t('pages.cameras.table.model')}</Table.Th>
                <Table.Th>{t('pages.cameras.table.user')}</Table.Th>
                <Table.Th>{t('pages.cameras.table.profile')}</Table.Th>
                <Table.Th>{t('pages.cameras.table.path')}</Table.Th>
                <Table.Th>{t('pages.cameras.table.status')}</Table.Th>
                <Table.Th>{t('pages.cameras.table.updated')}</Table.Th>
                <Table.Th>{t('pages.cameras.table.actions')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {pagedCameras.map((camera) => (
                <Table.Tr key={camera.cameraId}>
                  <Table.Td>{camera.cameraId}</Table.Td>
                  <Table.Td>{camera.code ?? '-'}</Table.Td>
                  <Table.Td>{camera.ipAddress}</Table.Td>
                  <Table.Td>{camera.cameraSeries ?? '-'}</Table.Td>
                  <Table.Td>{camera.cameraModel ?? '-'}</Table.Td>
                  <Table.Td>{camera.rtspUsername}</Table.Td>
                  <Table.Td>{camera.rtspProfile}</Table.Td>
                  <Table.Td>{camera.rtspPath}</Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      <Badge color={camera.enabled ? 'brand' : 'gray'} variant="light">
                        {camera.enabled ? t('common.status.enabled') : t('common.status.disabled')}
                      </Badge>
                      <Badge color={camera.hasPassword ? 'brand' : 'gray'} variant="light">
                        {camera.hasPassword
                          ? t('pages.cameras.badges.secret')
                          : t('pages.cameras.badges.noSecret')}
                      </Badge>
                    </Group>
                  </Table.Td>
                  <Table.Td>{formatDateTime(camera.updatedAt, t)}</Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      <Tooltip label={t('pages.cameras.actions.testRtsp')}>
                        <ActionIcon
                          variant="light"
                          color="brand"
                          onClick={() => testMutation.mutate(camera.cameraId)}
                          loading={testMutation.isPending}
                        >
                          <IconPlayerPlay size={16} />
                        </ActionIcon>
                      </Tooltip>
                      <Tooltip label={t('common.actions.edit')}>
                        <ActionIcon
                          variant="light"
                          color="amber"
                          onClick={() => openEdit(camera)}
                        >
                          <IconEdit size={16} />
                        </ActionIcon>
                      </Tooltip>
                      <Tooltip label={t('common.actions.delete')}>
                        <ActionIcon
                          variant="light"
                          color="red"
                          onClick={() => confirmDelete(camera)}
                        >
                          <IconTrash size={16} />
                        </ActionIcon>
                      </Tooltip>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
        <Group justify="space-between" align="center" mt="md">
          <Text size="xs" className="muted-text">
            {t('common.pagination.showing', {
              from: fromIndex,
              to: toIndex,
              total: totalCount
            })}
          </Text>
          <Group gap="xs">
            <Select
              data={[
                { value: '10', label: t('common.pagination.perPage', { count: 10 }) },
                { value: '20', label: t('common.pagination.perPage', { count: 20 }) },
                { value: '50', label: t('common.pagination.perPage', { count: 50 }) },
                { value: '100', label: t('common.pagination.perPage', { count: 100 }) }
              ]}
              value={pageSize}
              onChange={(value) => setPageSize(value ?? '20')}
              w={120}
              size="xs"
              allowDeselect={false}
            />
            <Pagination value={page} onChange={setPage} total={totalPages} size="sm" />
          </Group>
        </Group>
      </Paper>

      <Modal
        opened={scanOpened}
        onClose={closeScan}
        title={t('pages.cameras.scan.title')}
        size="xl"
      >
        <Stack gap="md">
          <Paper p="md" radius="lg" className="surface-card strong">
            <Stack gap="sm">
              <Group justify="space-between" align="center">
                <Stack gap={2}>
                  <Text size="sm" fw={600}>
                    {t('pages.cameras.scan.range.title')}
                  </Text>
                  <Text size="xs" className="muted-text">
                    {t('pages.cameras.scan.range.subtitle')}
                  </Text>
                </Stack>
                <Badge color={discovered.length > 0 ? 'brand' : 'gray'} variant="light">
                  {t('pages.cameras.scan.range.found', { count: discovered.length })}
                </Badge>
              </Group>
              <Group align="flex-end" grow>
                <TextInput
                  label={t('pages.cameras.scan.range.from')}
                  placeholder={t('pages.cameras.scan.range.fromPlaceholder')}
                  {...scanForm.getInputProps('ipStart')}
                />
                <TextInput
                  label={t('pages.cameras.scan.range.to')}
                  placeholder={t('pages.cameras.scan.range.toPlaceholder')}
                  {...scanForm.getInputProps('ipEnd')}
                />
                <Button
                  leftSection={<IconSearch size={16} />}
                  onClick={handleScan}
                  loading={scanMutation.isPending}
                >
                  {t('pages.cameras.actions.scan')}
                </Button>
              </Group>
            </Stack>
          </Paper>

          <Paper p="md" radius="lg" className="surface-card">
            <Group justify="space-between" align="center">
              <Stack gap={2}>
                <Text size="sm" fw={600}>
                  {t('pages.cameras.scan.discovered.title')}
                </Text>
                <Text size="xs" className="muted-text">
                  {t('pages.cameras.scan.discovered.subtitle')}
                </Text>
              </Stack>
              <Group gap="xs">
                <Badge color="brand" variant="light">
                  {t('pages.cameras.scan.discovered.ready', { count: readyCount })}
                </Badge>
                <Badge color="gray" variant="light">
                  {t('pages.cameras.scan.discovered.existing', { count: alreadyAddedCount })}
                </Badge>
                <Badge color="blue" variant="light">
                  {t('pages.cameras.scan.discovered.selected', { count: selectedCount })}
                </Badge>
              </Group>
            </Group>
            <Divider my="sm" />
            {discovered.length === 0 ? (
              <Text size="sm">{t('pages.cameras.scan.discovered.empty')}</Text>
            ) : (
              <ScrollArea h={360}>
                <Table highlightOnHover>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>
                        <Checkbox
                          aria-label={t('pages.cameras.scan.discovered.selectAll')}
                          checked={allSelected}
                          indeterminate={!allSelected && someSelected}
                          disabled={selectableKeys.length === 0}
                          onChange={(event) => toggleSelectAll(event.currentTarget.checked)}
                        />
                      </Table.Th>
                      <Table.Th>{t('pages.cameras.scan.table.name')}</Table.Th>
                      <Table.Th>{t('pages.cameras.scan.table.series')}</Table.Th>
                      <Table.Th>{t('pages.cameras.scan.table.model')}</Table.Th>
                      <Table.Th>{t('pages.cameras.scan.table.ip')}</Table.Th>
                      <Table.Th>{t('pages.cameras.scan.table.mac')}</Table.Th>
                      <Table.Th>{t('pages.cameras.scan.table.deviceId')}</Table.Th>
                      <Table.Th>{t('pages.cameras.scan.table.status')}</Table.Th>
                      <Table.Th>{t('pages.cameras.scan.table.actions')}</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {discoveredItems.map((item, index) => (
                      <Table.Tr key={item.key}>
                        <Table.Td>
                          {(() => {
                            const cameraLabel =
                              item.camera.name ??
                              item.camera.ipAddress ??
                              t('pages.cameras.scan.discovered.cameraIndex', { index: index + 1 });
                            return (
                              <Checkbox
                                aria-label={t('pages.cameras.scan.discovered.selectOne', {
                                  name: cameraLabel
                                })}
                                checked={selectedKeys.has(item.key)}
                                disabled={!item.isSelectable}
                                onChange={(event) =>
                                  toggleSelection(item.key, event.currentTarget.checked)
                                }
                              />
                            );
                          })()}
                        </Table.Td>
                        <Table.Td>{item.camera.name ?? '-'}</Table.Td>
                        <Table.Td>{item.camera.cameraSeries ?? '-'}</Table.Td>
                        <Table.Td>{item.camera.model ?? '-'}</Table.Td>
                        <Table.Td>{item.camera.ipAddress ?? '-'}</Table.Td>
                        <Table.Td>{item.camera.macAddress ?? '-'}</Table.Td>
                        <Table.Td>
                          <Text size="xs" className="muted-text">
                            {item.camera.deviceId ?? '-'}
                          </Text>
                        </Table.Td>
                        <Table.Td>
                          <Badge
                            color={item.isAdded ? 'gray' : item.isSelectable ? 'brand' : 'yellow'}
                            variant="light"
                          >
                            {item.isAdded
                              ? t('pages.cameras.scan.badges.alreadyAdded')
                              : item.isSelectable
                              ? t('pages.cameras.scan.badges.ready')
                              : t('pages.cameras.scan.badges.missingIp')}
                          </Badge>
                        </Table.Td>
                        <Table.Td>
                          <Tooltip
                            label={
                              item.isSelectable
                                ? t('pages.cameras.scan.actions.prefill')
                                : item.isAdded
                                ? t('pages.cameras.scan.actions.alreadyInSystem')
                                : t('pages.cameras.scan.actions.missingIp')
                            }
                          >
                            <ActionIcon
                              variant="light"
                              color="brand"
                              disabled={!item.isSelectable}
                              onClick={() => applyDiscovered(item.camera)}
                            >
                              <IconPlus size={16} />
                            </ActionIcon>
                          </Tooltip>
                        </Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </ScrollArea>
            )}
          </Paper>

          <Paper p="md" radius="lg" className="surface-card strong">
            <Group justify="space-between" align="center">
              <Stack gap={2}>
                <Text size="sm" fw={600}>
                  {t('pages.cameras.bulk.title')}
                </Text>
                <Text size="xs" className="muted-text">
                  {t('pages.cameras.bulk.subtitle')}
                </Text>
              </Stack>
              <Group>
                <Button
                  variant="subtle"
                  onClick={() => setSelectedKeys(new Set())}
                  disabled={selectedCount === 0}
                >
                  {t('common.actions.clearSelection')}
                </Button>
                <Button
                  leftSection={<IconPlus size={16} />}
                  onClick={handleBulkAdd}
                  loading={bulkAddMutation.isPending}
                  disabled={selectedCount === 0}
                >
                  {t('pages.cameras.bulk.actions.addSelected')}
                </Button>
              </Group>
            </Group>
            <Divider my="sm" />
            <Stack gap="sm">
              <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="md">
                <TextInput
                  label={t('pages.cameras.form.rtspUsername')}
                  placeholder={t('pages.cameras.form.placeholders.rtspUsername')}
                  {...bulkForm.getInputProps('rtspUsername')}
                />
                <TextInput
                  label={t('pages.cameras.form.rtspPassword')}
                  type="password"
                  placeholder={t('pages.cameras.form.placeholders.rtspPassword')}
                  {...bulkForm.getInputProps('rtspPassword')}
                />
                <TextInput
                  label={t('pages.cameras.form.rtspProfile')}
                  placeholder={t('pages.cameras.form.placeholders.rtspProfile')}
                  {...bulkForm.getInputProps('rtspProfile')}
                />
                <TextInput
                  label={t('pages.cameras.form.rtspPath')}
                  placeholder={t('pages.cameras.form.placeholders.rtspPath')}
                  {...bulkForm.getInputProps('rtspPath')}
                />
              </SimpleGrid>
              <Switch
                label={t('pages.cameras.bulk.enabled')}
                checked={bulkForm.values.enabled}
                onChange={(event) =>
                  bulkForm.setFieldValue('enabled', event.currentTarget.checked)
                }
              />
            </Stack>
          </Paper>
        </Stack>
      </Modal>

      <Modal
        opened={opened}
        onClose={close}
        title={
          editing ? t('pages.cameras.modals.edit.title') : t('pages.cameras.modals.create.title')
        }
        size="lg"
      >
        <form onSubmit={form.onSubmit(onSubmit)}>
          <Stack gap="md">
            <TextInput
              label={t('pages.cameras.form.cameraId')}
              placeholder={t('pages.cameras.form.placeholders.cameraId')}
              disabled={Boolean(editing)}
              {...form.getInputProps('cameraId')}
            />
            <TextInput
              label={t('pages.cameras.form.code')}
              placeholder={t('pages.cameras.form.placeholders.code')}
              {...form.getInputProps('code')}
            />
            <TextInput
              label={t('pages.cameras.form.ipAddress')}
              placeholder={t('pages.cameras.form.placeholders.ipAddress')}
              {...form.getInputProps('ipAddress')}
            />
            <Group grow>
              <TextInput
                label={t('pages.cameras.form.series')}
                placeholder={t('pages.cameras.form.placeholders.series')}
                {...form.getInputProps('cameraSeries')}
              />
              <TextInput
                label={t('pages.cameras.form.model')}
                placeholder={t('pages.cameras.form.placeholders.model')}
                {...form.getInputProps('cameraModel')}
              />
            </Group>
            <TextInput
              label={t('pages.cameras.form.rtspUsername')}
              placeholder={t('pages.cameras.form.placeholders.rtspUsername')}
              {...form.getInputProps('rtspUsername')}
            />
            <TextInput
              label={t('pages.cameras.form.rtspPassword')}
              placeholder={
                editing
                  ? t('pages.cameras.form.placeholders.rtspPasswordKeep')
                  : t('pages.cameras.form.placeholders.rtspPassword')
              }
              type="password"
              {...form.getInputProps('rtspPassword')}
            />
            <Group grow>
              <TextInput
                label={t('pages.cameras.form.rtspProfile')}
                placeholder={t('pages.cameras.form.placeholders.rtspProfile')}
                {...form.getInputProps('rtspProfile')}
              />
              <TextInput
                label={t('pages.cameras.form.rtspPath')}
                placeholder={t('pages.cameras.form.placeholders.rtspPath')}
                {...form.getInputProps('rtspPath')}
              />
            </Group>
            <Switch
              label={t('pages.cameras.form.enabled')}
              checked={form.values.enabled}
              onChange={(event) => form.setFieldValue('enabled', event.currentTarget.checked)}
            />
            <Group justify="flex-end">
              <Button variant="subtle" leftSection={<IconX size={16} />} onClick={close}>
                {t('common.actions.cancel')}
              </Button>
              <Button
                type="submit"
                leftSection={<IconCheck size={16} />}
                loading={saveMutation.isPending}
              >
                {t('common.actions.save')}
              </Button>
            </Group>
          </Stack>
        </form>
      </Modal>
    </Stack>
  );
}
