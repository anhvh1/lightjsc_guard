import {
  ActionIcon,
  Badge,
  Box,
  Button,
  Center,
  Divider,
  FileInput,
  Group,
  Image,
  Modal,
  NumberInput,
  Paper,
  ScrollArea,
  Select,
  SimpleGrid,
  Slider,
  Stack,
  Switch,
  Tabs,
  Text,
  TextInput,
  Textarea,
  Tooltip
} from '@mantine/core';
import { useForm } from '@mantine/form';
import { useDisclosure } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import * as signalR from '@microsoft/signalr';
import { IconCheck, IconCrop, IconMapPin, IconRadar, IconUserPlus, IconX } from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useState } from 'react';
import Cropper, { type Area } from 'react-easy-crop';
import { createPerson, enrollPerson, listCameras } from '../api/ingestor';
import { buildSubscriberUrl, fetchFaceSnapshot, getSubscriberBaseUrl } from '../api/subscriber';
import type {
  CameraResponse,
  EnrollFaceRequest,
  FaceEvent,
  FaceEventSnapshot,
  PersonRequest
} from '../api/types';
import { FaceStreamMapPanel } from '../components/FaceStreamMapPanel';
import { useI18n } from '../i18n/I18nProvider';
import { formatDateTime } from '../utils/format';

const MAX_ITEMS = 50;

type ConnectionState = 'connecting' | 'live' | 'reconnecting' | 'disconnected' | 'failed';

const defaultPersonValues: PersonRequest = {
  code: '',
  firstName: '',
  lastName: '',
  gender: '',
  age: null,
  remarks: '',
  category: '',
  listType: '',
  isActive: true
};

type EnrollFormValues = {
  cameraId: string;
  imageFile: File | null;
  storeFaceImage: boolean;
  sourceCameraId: string;
};

const defaultEnrollValues: EnrollFormValues = {
  cameraId: '',
  imageFile: null,
  storeFaceImage: true,
  sourceCameraId: ''
};

const connectionMeta: Record<ConnectionState, { labelKey: string; color: string }> = {
  connecting: { labelKey: 'pages.faceStream.connection.connecting', color: 'yellow' },
  live: { labelKey: 'pages.faceStream.connection.live', color: 'brand' },
  reconnecting: { labelKey: 'pages.faceStream.connection.reconnecting', color: 'orange' },
  disconnected: { labelKey: 'pages.faceStream.connection.disconnected', color: 'gray' },
  failed: { labelKey: 'pages.faceStream.connection.failed', color: 'red' }
};

const trimEvents = (events: FaceEvent[]) => events.slice(0, MAX_ITEMS);

const formatValue = (value?: string | number | null) => {
  if (value === null || value === undefined || value === '') {
    return '-';
  }
  return `${value}`;
};

const resolveFaceImage = (value?: string | null) => {
  if (!value) {
    return null;
  }
  return value.startsWith('data:image') ? value : `data:image/jpeg;base64,${value}`;
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

function DetailRow({ label, value }: { label: string; value?: string | number | null }) {
  return (
    <Group justify="flex-start" gap={6} wrap="nowrap">
      <Text size="xs" className="muted-text">
        {label}:
      </Text>
      <Text size="xs" fw={500} lineClamp={1}>
        {formatValue(value)}
      </Text>
    </Group>
  );
}

function FaceCard({
  event,
  cameraLabel,
  onCreatePerson,
  onViewMap
}: {
  event: FaceEvent;
  cameraLabel?: string;
  onCreatePerson?: (event: FaceEvent) => void;
  onViewMap?: (event: FaceEvent) => void;
}) {
  const { t } = useI18n();
  const faceImage = resolveFaceImage(event.faceImageBase64);
  const headerLabel =
    cameraLabel || event.cameraName || event.cameraId || t('common.fields.camera');
  const showCreateButton = Boolean(onCreatePerson) && !event.isKnown;
  const showMapButton = Boolean(onViewMap);
  const details = [
    { label: t('common.fields.time'), value: formatDateTime(event.eventTimeUtc, t) },
    { label: t('common.fields.camera'), value: cameraLabel ?? event.cameraId },
    { label: t('common.fields.zone'), value: event.zone },
    { label: t('common.fields.age'), value: event.age },
    { label: t('common.fields.gender'), value: event.gender },
    { label: t('common.fields.mask'), value: event.mask },
    { label: t('common.fields.score'), value: event.scoreText },
    { label: t('common.fields.similarity'), value: event.similarityText },
    { label: t('common.fields.watchlist'), value: event.watchlistEntryId },
    { label: t('common.fields.person'), value: formatPersonName(event) },
    { label: t('common.fields.category'), value: event.person?.category },
    { label: t('common.fields.remarks'), value: event.person?.remarks }
  ];

  return (
    <Paper p="md" radius="lg" className="surface-card strong">
      <Group align="flex-start" wrap="nowrap">
        <Stack gap={6} align="center" style={{ width: 96 }}>
          <Box>
            {faceImage ? (
              <Image src={faceImage} w={96} h={96} radius="md" fit="cover" />
            ) : (
              <Center
                w={96}
                h={96}
                className="surface-card"
                style={{ borderRadius: 12, borderStyle: 'dashed' }}
              >
                <Text size="xs" className="muted-text" ta="center">
                  {t('common.empty.noImage')}
                </Text>
              </Center>
            )}
          </Box>
          {(showMapButton || showCreateButton) && (
            <Group gap={6} justify="center">
              {showMapButton && (
                <Tooltip label={t('pages.faceStream.actions.trackOnMap')} position="bottom" withArrow>
                  <ActionIcon
                    variant="light"
                    color="brand"
                    onClick={() => onViewMap?.(event)}
                    aria-label={t('pages.faceStream.actions.trackOnMap')}
                  >
                    <IconMapPin size={16} />
                  </ActionIcon>
                </Tooltip>
              )}
              {showCreateButton && (
                <Tooltip label={t('pages.faceStream.actions.createPerson')} position="bottom" withArrow>
                  <ActionIcon
                    variant="light"
                    color="brand"
                    onClick={() => onCreatePerson?.(event)}
                    aria-label={t('pages.faceStream.actions.createPerson')}
                  >
                    <IconUserPlus size={16} />
                  </ActionIcon>
                </Tooltip>
              )}
            </Group>
          )}
        </Stack>

        <Stack gap="xs" style={{ flex: 1 }}>
          <Group justify="space-between" align="center">
            <Text size="sm" fw={600}>
              {headerLabel}
            </Text>
            <Badge color={event.isKnown ? 'brand' : 'orange'} variant="light">
              {event.isKnown ? t('common.status.known') : t('common.status.unknown')}
            </Badge>
          </Group>
          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
            {details.map((detail) => (
              <DetailRow key={detail.label} label={detail.label} value={detail.value} />
            ))}
          </SimpleGrid>
        </Stack>
      </Group>
    </Paper>
  );
}

export function FaceStream() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const [known, setKnown] = useState<FaceEvent[]>([]);
  const [unknown, setUnknown] = useState<FaceEvent[]>([]);
  const [knownTotal, setKnownTotal] = useState(0);
  const [unknownTotal, setUnknownTotal] = useState(0);
  const [connectionState, setConnectionState] = useState<ConnectionState>('connecting');
  const [lastError, setLastError] = useState<string | null>(null);
  const [createEnrollEnabled, setCreateEnrollEnabled] = useState(false);
  const [createImagePreview, setCreateImagePreview] = useState<string | null>(null);
  const [createCrop, setCreateCrop] = useState({ x: 0, y: 0 });
  const [createZoom, setCreateZoom] = useState(1);
  const [createCroppedAreaPixels, setCreateCroppedAreaPixels] = useState<Area | null>(null);
  const [createCroppedPreview, setCreateCroppedPreview] = useState<string | null>(null);
  const [createCroppedBase64, setCreateCroppedBase64] = useState<string | null>(null);
  const [createCropError, setCreateCropError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<'stream' | 'map'>('stream');
  const [trackedFace, setTrackedFace] = useState<FaceEvent | null>(null);

  const [createPersonOpened, createPersonModal] = useDisclosure(false);

  const subscriberTarget = useMemo(() => getSubscriberBaseUrl() || 'same-origin', []);
  const hubUrl = useMemo(() => buildSubscriberUrl('/hubs/faces'), []);

  const camerasQuery = useQuery({
    queryKey: ['cameras'],
    queryFn: listCameras
  });

  const cameraOptions = useMemo(() => {
    return (camerasQuery.data ?? []).map((camera: CameraResponse) => ({
      value: camera.cameraId,
      label: `${camera.code?.trim() || camera.cameraId}${camera.ipAddress ? ` - ${camera.ipAddress}` : ''}`
    }));
  }, [camerasQuery.data]);

  const cameraLabelById = useMemo(() => {
    return new Map(
      (camerasQuery.data ?? []).map((camera) => [
        camera.cameraId,
        camera.code?.trim() || camera.cameraId
      ])
    );
  }, [camerasQuery.data]);

  const resolveCameraLabel = (cameraId?: string | null) => {
    if (!cameraId) {
      return t('common.fields.camera');
    }
    return cameraLabelById.get(cameraId) ?? cameraId;
  };

  const listTypeOptions = useMemo(
    () => [
      { value: '', label: t('pages.faceStream.form.listType.protect') },
      { value: 'WhiteList', label: t('pages.faceStream.form.listType.white') },
      { value: 'BlackList', label: t('pages.faceStream.form.listType.black') }
    ],
    [t]
  );

  const hasCameras = cameraOptions.length > 0;

  const personForm = useForm<PersonRequest>({
    initialValues: defaultPersonValues,
    validate: {
      code: (value) =>
        value.trim().length === 0 ? t('pages.faceStream.validation.codeRequired') : null,
      firstName: (value) =>
        value.trim().length === 0 ? t('pages.faceStream.validation.firstNameRequired') : null,
      lastName: (value) =>
        value.trim().length === 0 ? t('pages.faceStream.validation.lastNameRequired') : null
    }
  });

  const createEnrollForm = useForm<EnrollFormValues>({
    initialValues: defaultEnrollValues,
    validate: {
      cameraId: (value) =>
        createEnrollEnabled && value.trim().length === 0
          ? t('pages.faceStream.validation.cameraRequired')
          : null,
      imageFile: (value) =>
        createEnrollEnabled && !value ? t('pages.faceStream.validation.faceImageRequired') : null
    }
  });

  const resetCreateCropState = () => {
    setCreateCrop({ x: 0, y: 0 });
    setCreateZoom(1);
    setCreateCroppedAreaPixels(null);
    setCreateCroppedPreview(null);
    setCreateCroppedBase64(null);
    setCreateCropError(null);
  };

  const resetCreateEnrollState = () => {
    setCreateEnrollEnabled(false);
    createEnrollForm.setValues(defaultEnrollValues);
    createEnrollForm.resetDirty();
    createEnrollForm.clearErrors();
    setCreateImagePreview(null);
    resetCreateCropState();
  };

  const onCreateCropComplete = (_: Area, areaPixels: Area) => {
    setCreateCroppedAreaPixels(areaPixels);
  };

  useEffect(() => {
    resetCreateCropState();
    if (!createEnrollEnabled || !createEnrollForm.values.imageFile) {
      setCreateImagePreview(null);
      return;
    }

    const url = URL.createObjectURL(createEnrollForm.values.imageFile);
    setCreateImagePreview(url);
    return () => URL.revokeObjectURL(url);
  }, [createEnrollEnabled, createEnrollForm.values.imageFile]);

  const applyCreateCrop = async () => {
    if (!createImagePreview || !createCroppedAreaPixels) {
      setCreateCropError(t('pages.faceStream.crop.selectArea'));
      return;
    }

    try {
      const dataUrl = await getCroppedImageDataUrl(createImagePreview, createCroppedAreaPixels);
      setCreateCroppedPreview(dataUrl);
      setCreateCroppedBase64(dataUrl);
      setCreateCropError(null);
    } catch (error) {
      setCreateCropError(t('pages.faceStream.crop.failed'));
    }
  };

  const clearCreateCrop = () => {
    setCreateCroppedPreview(null);
    setCreateCroppedBase64(null);
    setCreateCropError(null);
  };

  const createMutation = useMutation({
    mutationFn: async (params: { person: PersonRequest; enrollPayload?: EnrollFaceRequest | null }) => {
      const created = await createPerson(params.person);
      if (!params.enrollPayload) {
        return { person: created, enrolled: false, enrollFailed: false };
      }

      try {
        await enrollPerson(created.id, params.enrollPayload);
        return { person: created, enrolled: true, enrollFailed: false };
      } catch (error) {
        return { person: created, enrolled: false, enrollFailed: true, enrollError: error as Error };
      }
    },
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ['persons'] });
      if (result.enrollFailed) {
        const message =
          result.enrollError?.message ?? t('pages.faceStream.notifications.enrollFailed.message');
        notifications.show({
          title: t('pages.faceStream.notifications.personCreated.title'),
          message: t('pages.faceStream.notifications.personCreated.retry', { message }),
          color: 'yellow'
        });
      } else {
        notifications.show({
          title: result.enrolled
            ? t('pages.faceStream.notifications.personCreatedEnrolled.title')
            : t('pages.faceStream.notifications.personCreated.title'),
          message: result.enrolled
            ? t('pages.faceStream.notifications.personCreatedEnrolled.message')
            : t('pages.faceStream.notifications.personCreated.message'),
          color: 'brand'
        });
      }
      closeCreateModal();
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.faceStream.notifications.createFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const isSaving = createMutation.isPending;

  const toggleCreateEnroll = (checked: boolean) => {
    setCreateEnrollEnabled(checked);
    if (!checked) {
      createEnrollForm.setValues(defaultEnrollValues);
      createEnrollForm.resetDirty();
      createEnrollForm.clearErrors();
      resetCreateCropState();
      return;
    }

    const firstCamera = camerasQuery.data?.[0]?.cameraId ?? '';
    createEnrollForm.setValues({
      ...createEnrollForm.values,
      cameraId: createEnrollForm.values.cameraId || firstCamera,
      sourceCameraId: createEnrollForm.values.sourceCameraId || firstCamera
    });
  };

  const closeCreateModal = () => {
    createPersonModal.close();
    personForm.setValues(defaultPersonValues);
    personForm.resetDirty();
    personForm.clearErrors();
    resetCreateEnrollState();
  };

  const openCreateFromEvent = (event: FaceEvent) => {
    personForm.setValues(defaultPersonValues);
    personForm.resetDirty();
    personForm.clearErrors();
    resetCreateEnrollState();

    const fallbackCamera = camerasQuery.data?.[0]?.cameraId ?? '';
    const cameraId = event.cameraId || fallbackCamera;
    const faceDataUrl = resolveFaceImage(event.faceImageBase64);
    let faceFile: File | null = null;

    if (faceDataUrl && hasCameras) {
      try {
        faceFile = dataUrlToFile(faceDataUrl, `unknown-face-${event.id}.jpg`);
      } catch (error) {
        notifications.show({
          title: t('pages.faceStream.notifications.faceImageError.title'),
          message:
            (error as Error).message ??
            t('pages.faceStream.notifications.faceImageError.message'),
          color: 'yellow'
        });
      }
    }

    createEnrollForm.setValues({
      ...defaultEnrollValues,
      cameraId,
      sourceCameraId: cameraId,
      imageFile: faceFile
    });
    setCreateEnrollEnabled(Boolean(faceFile));

    createPersonModal.open();
  };

  const onCreateSubmit = async (values: PersonRequest) => {
    const normalized = {
      ...values,
      gender: values.gender?.trim() || undefined,
      remarks: values.remarks?.trim() || undefined,
      category: values.category?.trim() || undefined,
      listType: values.listType?.trim() || undefined
    };

    if (createEnrollEnabled) {
      const validation = createEnrollForm.validate();
      if (validation.hasErrors) {
        return;
      }
    }

    let enrollPayload: EnrollFaceRequest | null = null;
    if (createEnrollEnabled) {
      const imageFile = createEnrollForm.values.imageFile;
      if (!imageFile) {
        createEnrollForm.setFieldError(
          'imageFile',
          t('pages.faceStream.validation.faceImageRequired')
        );
        return;
      }

      try {
        const base64 = createCroppedBase64 ?? (await fileToBase64(imageFile));
        enrollPayload = {
          cameraId: createEnrollForm.values.cameraId,
          imageBase64: base64,
          storeFaceImage: createEnrollForm.values.storeFaceImage,
          sourceCameraId: createEnrollForm.values.sourceCameraId.trim() || undefined
        };
      } catch (error) {
        notifications.show({
          title: t('pages.faceStream.notifications.enrollFailed.title'),
          message:
            (error as Error).message ?? t('pages.faceStream.notifications.enrollFailed.message'),
          color: 'red'
        });
        return;
      }
    }

    createMutation.mutate({ person: normalized, enrollPayload });
  };

  const applySnapshot = useCallback((snapshot: FaceEventSnapshot) => {
    const knownItems = trimEvents(snapshot.known ?? []);
    const unknownItems = trimEvents(snapshot.unknown ?? []);
    setKnown(knownItems);
    setUnknown(unknownItems);
    setKnownTotal(snapshot.knownTotal ?? snapshot.known?.length ?? 0);
    setUnknownTotal(snapshot.unknownTotal ?? snapshot.unknown?.length ?? 0);
  }, []);

  const pushEvent = useCallback((event: FaceEvent) => {
    if (event.isKnown) {
      setKnown((prev) => trimEvents([event, ...prev]));
      setKnownTotal((prev) => prev + 1);
    } else {
      setUnknown((prev) => trimEvents([event, ...prev]));
      setUnknownTotal((prev) => prev + 1);
    }
  }, []);

  const loadSnapshot = useCallback(async () => {
    try {
      const snapshot = await fetchFaceSnapshot();
      applySnapshot(snapshot);
      setLastError(null);
    } catch (error) {
      setLastError(
        error instanceof Error ? error.message : t('pages.faceStream.errors.loadSnapshot')
      );
    }
  }, [applySnapshot]);

  useEffect(() => {
    let active = true;
    let retryTimer: number | undefined;
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect([1000, 3000, 5000, 10000])
      .build();

    connection.on('snapshot', (snapshot: FaceEventSnapshot) => {
      if (!active) return;
      applySnapshot(snapshot);
    });

    connection.on('faceEvent', (event: FaceEvent) => {
      if (!active) return;
      pushEvent(event);
    });

    connection.onreconnecting(() => {
      if (!active) return;
      setConnectionState('reconnecting');
    });

    connection.onreconnected(() => {
      if (!active) return;
      setConnectionState('live');
    });

    connection.onclose(() => {
      if (!active) return;
      setConnectionState('disconnected');
    });

    const startConnection = async () => {
      setConnectionState('connecting');
      try {
        await connection.start();
        if (!active) return;
        setConnectionState('live');
        setLastError(null);
      } catch (error) {
        if (!active) return;
        setConnectionState('failed');
        setLastError(
          error instanceof Error ? error.message : t('pages.faceStream.errors.connectFailed')
        );
        retryTimer = window.setTimeout(startConnection, 3000);
      }
    };

    loadSnapshot();
    startConnection();

    return () => {
      active = false;
      if (retryTimer) {
        window.clearTimeout(retryTimer);
      }
      void connection.stop();
    };
  }, [applySnapshot, hubUrl, loadSnapshot, pushEvent]);

  useEffect(() => {
    if (connectionState === 'live') {
      return;
    }

    const timer = window.setInterval(() => {
      loadSnapshot();
    }, 3000);

    return () => window.clearInterval(timer);
  }, [connectionState, loadSnapshot]);

  const connectionBadge = connectionMeta[connectionState];
  const knownCount = knownTotal > 0 ? knownTotal : known.length;
  const unknownCount = unknownTotal > 0 ? unknownTotal : unknown.length;

  const handleViewOnMap = useCallback((event: FaceEvent) => {
    setTrackedFace(event);
    setActiveTab('map');
  }, []);

  useEffect(() => {
    if (!trackedFace?.isKnown || !trackedFace.personId) {
      return;
    }
    const personId = trackedFace.personId;
    const match = known.find((event) => event.personId === personId);
    if (match && match.id !== trackedFace.id) {
      setTrackedFace(match);
    }
  }, [known, trackedFace]);

  return (
    <Stack gap="lg" className="page">
      <Group justify="space-between" align="flex-start">
        <Stack gap={4}>
          <Text size="sm" className="muted-text">
            {t('pages.faceStream.subtitle')}
          </Text>
          <Text size="xl" fw={600}>
            {t('pages.faceStream.title')}
          </Text>
        </Stack>
        <Stack gap={6} align="flex-end">
          <Badge color={connectionBadge.color} variant="light">
            {t(connectionBadge.labelKey)}
          </Badge>
          <Text size="sm" className="muted-text">
            {t('pages.faceStream.subscriberTarget', { target: subscriberTarget })}
          </Text>
          {lastError && (
            <Text size="xs" c="red">
              {lastError}
            </Text>
          )}
        </Stack>
      </Group>

      <Tabs
        value={activeTab}
        onChange={(value) => value && setActiveTab(value as 'stream' | 'map')}
        variant="pills"
      >
        <Tabs.List>
          <Tabs.Tab value="stream" leftSection={<IconRadar size={16} />}>
            {t('pages.faceStream.tabs.stream')}
          </Tabs.Tab>
          <Tabs.Tab value="map" leftSection={<IconMapPin size={16} />}>
            {t('pages.faceStream.tabs.map')}
          </Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="stream" pt="md">
          <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="lg">
            <Paper p="lg" radius="lg" className="surface-card">
              <Group justify="space-between" align="center">
                <Stack gap={4}>
                  <Text size="sm" className="muted-text">
                    {t('pages.faceStream.known.subtitle')}
                  </Text>
                  <Text size="lg" fw={600}>
                    {t('pages.faceStream.known.title')}
                  </Text>
                </Stack>
                <Badge color="brand" variant="light">
                  {knownCount}
                </Badge>
              </Group>
              <Divider my="md" />
              <ScrollArea type="auto" h="calc(100vh - 320px)">
                <Stack gap="md">
                  {known.length === 0 ? (
                    <Text size="sm" className="muted-text">
                      {t('pages.faceStream.known.empty')}
                    </Text>
                  ) : (
                    known.map((event) => (
                      <FaceCard
                        key={event.id}
                        event={event}
                        cameraLabel={resolveCameraLabel(event.cameraId)}
                        onViewMap={handleViewOnMap}
                      />
                    ))
                  )}
                </Stack>
              </ScrollArea>
            </Paper>

            <Paper p="lg" radius="lg" className="surface-card">
              <Group justify="space-between" align="center">
                <Stack gap={4}>
                  <Text size="sm" className="muted-text">
                    {t('pages.faceStream.unknown.subtitle')}
                  </Text>
                  <Text size="lg" fw={600}>
                    {t('pages.faceStream.unknown.title')}
                  </Text>
                </Stack>
                <Badge color="orange" variant="light">
                  {unknownCount}
                </Badge>
              </Group>
              <Divider my="md" />
              <ScrollArea type="auto" h="calc(100vh - 320px)">
                <Stack gap="md">
                  {unknown.length === 0 ? (
                    <Text size="sm" className="muted-text">
                      {t('pages.faceStream.unknown.empty')}
                    </Text>
                  ) : (
                    unknown.map((event) => (
                      <FaceCard
                        key={event.id}
                        event={event}
                        cameraLabel={resolveCameraLabel(event.cameraId)}
                        onCreatePerson={openCreateFromEvent}
                        onViewMap={handleViewOnMap}
                      />
                    ))
                  )}
                </Stack>
              </ScrollArea>
            </Paper>
          </SimpleGrid>
        </Tabs.Panel>

        <Tabs.Panel value="map" pt="md">
          <FaceStreamMapPanel seedEvent={trackedFace} isActive={activeTab === 'map'} />
        </Tabs.Panel>
      </Tabs>
      <Modal
        opened={createPersonOpened}
        onClose={closeCreateModal}
        title={t('pages.faceStream.modals.addPerson.title')}
        size="lg"
      >
        <form onSubmit={personForm.onSubmit(onCreateSubmit)}>
          <Stack gap="md">
            <Group grow>
              <TextInput
                label={t('pages.faceStream.form.code')}
                placeholder={t('pages.faceStream.form.placeholders.code')}
                {...personForm.getInputProps('code')}
              />
              <TextInput
                label={t('pages.faceStream.form.category')}
                placeholder={t('pages.faceStream.form.placeholders.category')}
                {...personForm.getInputProps('category')}
              />
            </Group>
            <Select
              label={t('pages.faceStream.form.listType.label')}
              placeholder={t('pages.faceStream.form.listType.placeholder')}
              data={listTypeOptions}
              value={personForm.values.listType ?? ''}
              onChange={(value) => personForm.setFieldValue('listType', value ?? '')}
            />
            <Group grow>
              <TextInput
                label={t('pages.faceStream.form.firstName')}
                placeholder={t('pages.faceStream.form.placeholders.firstName')}
                {...personForm.getInputProps('firstName')}
              />
              <TextInput
                label={t('pages.faceStream.form.lastName')}
                placeholder={t('pages.faceStream.form.placeholders.lastName')}
                {...personForm.getInputProps('lastName')}
              />
            </Group>
            <Group grow>
              <TextInput
                label={t('pages.faceStream.form.gender')}
                placeholder={t('pages.faceStream.form.placeholders.gender')}
                {...personForm.getInputProps('gender')}
              />
              <NumberInput
                label={t('pages.faceStream.form.age')}
                placeholder={t('pages.faceStream.form.placeholders.age')}
                min={0}
                max={120}
                {...personForm.getInputProps('age')}
              />
            </Group>
            <Textarea
              label={t('pages.faceStream.form.remarks')}
              placeholder={t('pages.faceStream.form.placeholders.remarks')}
              autosize
              minRows={2}
              {...personForm.getInputProps('remarks')}
            />
            <Paper p="md" radius="lg" className="surface-card strong">
              <Stack gap="sm">
                <Group justify="space-between" align="center">
                  <Stack gap={2}>
                    <Text size="sm" fw={600}>
                      {t('pages.faceStream.enroll.title')}
                    </Text>
                    <Text size="xs" className="muted-text">
                      {t('pages.faceStream.enroll.subtitle')}
                    </Text>
                  </Stack>
                  <Switch
                    checked={createEnrollEnabled}
                    onChange={(event) => toggleCreateEnroll(event.currentTarget.checked)}
                    disabled={!hasCameras}
                  />
                </Group>
                {!hasCameras && (
                  <Text size="xs" c="red">
                    {t('pages.faceStream.enroll.noCamera')}
                  </Text>
                )}
                {createEnrollEnabled && (
                  <Stack gap="sm">
                    <Select
                      label={t('pages.faceStream.enroll.camera')}
                      placeholder={t('pages.faceStream.enroll.cameraPlaceholder')}
                      data={cameraOptions}
                      value={createEnrollForm.values.cameraId}
                      onChange={(value) => {
                        const next = value ?? '';
                        createEnrollForm.setFieldValue('cameraId', next);
                        if (!createEnrollForm.values.sourceCameraId) {
                          createEnrollForm.setFieldValue('sourceCameraId', next);
                        }
                      }}
                      error={createEnrollForm.errors.cameraId}
                      searchable
                      nothingFoundMessage={t('pages.faceStream.enroll.noCameras')}
                    />
                    <FileInput
                      label={t('pages.faceStream.enroll.faceImage')}
                      placeholder={t('pages.faceStream.enroll.faceImagePlaceholder')}
                      accept="image/*"
                      clearable
                      value={createEnrollForm.values.imageFile}
                      onChange={(file) => createEnrollForm.setFieldValue('imageFile', file)}
                      error={createEnrollForm.errors.imageFile}
                    />
                    {createImagePreview && (
                      <Stack gap="sm">
                        <Divider label={t('pages.faceStream.enroll.cropTitle')} labelPosition="center" />
                        <Box
                          className="surface-card strong"
                          style={{
                            position: 'relative',
                            width: '100%',
                            height: 320,
                            borderRadius: 12,
                            overflow: 'hidden'
                          }}
                        >
                          <Cropper
                            image={createImagePreview}
                            crop={createCrop}
                            zoom={createZoom}
                            aspect={1}
                            onCropChange={setCreateCrop}
                            onZoomChange={setCreateZoom}
                            onCropComplete={onCreateCropComplete}
                          />
                        </Box>
                        <Group align="center" gap="md" wrap="nowrap">
                          <Text size="sm" className="muted-text">
                            {t('pages.faceStream.enroll.zoom')}
                          </Text>
                          <Slider
                            value={createZoom}
                            onChange={setCreateZoom}
                            min={1}
                            max={3}
                            step={0.1}
                            style={{ flex: 1 }}
                          />
                          <Button
                            size="xs"
                            variant="light"
                            leftSection={<IconCrop size={14} />}
                            onClick={applyCreateCrop}
                          >
                            {t('common.actions.apply')}
                          </Button>
                          {createCroppedPreview && (
                            <Button size="xs" variant="subtle" onClick={clearCreateCrop}>
                              {t('pages.faceStream.enroll.useOriginal')}
                            </Button>
                          )}
                        </Group>
                        {createCropError && (
                          <Text size="sm" c="red">
                            {createCropError}
                          </Text>
                        )}
                        {createCroppedPreview && (
                          <Paper p="sm" radius="md" className="surface-card strong">
                            <img
                              src={createCroppedPreview}
                              alt={t('pages.faceStream.enroll.croppedAlt')}
                              style={{ width: '100%', maxHeight: 220, objectFit: 'contain' }}
                            />
                          </Paper>
                        )}
                      </Stack>
                    )}
                    <TextInput
                      label={t('pages.faceStream.enroll.sourceCamera')}
                      placeholder={t('pages.faceStream.enroll.sourceCameraPlaceholder')}
                      value={createEnrollForm.values.sourceCameraId}
                      onChange={(event) =>
                        createEnrollForm.setFieldValue(
                          'sourceCameraId',
                          event.currentTarget.value
                        )
                      }
                    />
                    <Switch
                      label={t('pages.faceStream.enroll.storeFaceImage')}
                      checked={createEnrollForm.values.storeFaceImage}
                      onChange={(event) =>
                        createEnrollForm.setFieldValue(
                          'storeFaceImage',
                          event.currentTarget.checked
                        )
                      }
                    />
                  </Stack>
                )}
              </Stack>
            </Paper>
            <Switch
              label={t('common.status.active')}
              checked={personForm.values.isActive}
              onChange={(event) =>
                personForm.setFieldValue('isActive', event.currentTarget.checked)
              }
            />
            <Group justify="flex-end">
              <Button
                variant="subtle"
                leftSection={<IconX size={16} />}
                onClick={closeCreateModal}
              >
                {t('common.actions.cancel')}
              </Button>
              <Button
                type="submit"
                leftSection={<IconCheck size={16} />}
                loading={isSaving}
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

function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ''));
    reader.onerror = () => reject(new Error('Failed to read image file.'));
    reader.readAsDataURL(file);
  });
}

function createImage(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new window.Image();
    image.addEventListener('load', () => resolve(image));
    image.addEventListener('error', () => reject(new Error('Failed to load image.')));
    image.src = url;
  });
}

async function getCroppedImageDataUrl(imageSrc: string, cropArea: Area): Promise<string> {
  const image = await createImage(imageSrc);
  const canvas = document.createElement('canvas');
  const width = Math.max(1, Math.round(cropArea.width));
  const height = Math.max(1, Math.round(cropArea.height));
  const x = Math.round(cropArea.x);
  const y = Math.round(cropArea.y);

  canvas.width = width;
  canvas.height = height;

  const ctx = canvas.getContext('2d');
  if (!ctx) {
    throw new Error('Failed to create canvas.');
  }

  ctx.drawImage(image, x, y, width, height, 0, 0, width, height);
  return canvas.toDataURL('image/jpeg', 0.9);
}

function dataUrlToFile(dataUrl: string, filename: string): File {
  const parts = dataUrl.split(',');
  if (parts.length < 2) {
    throw new Error('Invalid image data.');
  }

  const match = parts[0].match(/data:(.*?);base64/);
  const mime = match?.[1] ?? 'image/jpeg';
  const binary = atob(parts[1]);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) {
    bytes[i] = binary.charCodeAt(i);
  }

  return new File([bytes], filename, { type: mime });
}
