import {
  ActionIcon,
  Badge,
  Box,
  Button,
  Divider,
  FileInput,
  Grid,
  Group,
  Image,
  Modal,
  MultiSelect,
  NumberInput,
  Paper,
  Pagination,
  Radio,
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
import {
  IconCheck,
  IconChevronDown,
  IconChevronUp,
  IconClock,
  IconFilter,
  IconCrop,
  IconMapPin,
  IconRadar,
  IconRefresh,
  IconSearch,
  IconTargetArrow,
  IconUpload,
  IconUserPlus,
  IconX
} from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import Cropper, { type Area } from 'react-easy-crop';
import { FaceTraceMapPanel } from '../components/FaceTraceMapPanel';
import {
  createPerson,
  detectPersonFaces,
  enrollPerson,
  listCameras,
  listPersons,
  searchFaceEvents,
  traceFaceByEvent,
  traceFaceByImage,
  traceFaceByPerson
} from '../api/ingestor';
import type {
  CameraResponse,
  EnrollFaceRequest,
  FaceEventRecord,
  FaceEventSearchFilter,
  FaceEventSearchRequest,
  FaceDetectResponse,
  PersonRequest,
  PersonResponse
} from '../api/types';
import { useI18n } from '../i18n/I18nProvider';
import { formatDateTime } from '../utils/format';

type TraceMode = 'search' | 'trace';
type TraceSource = 'person' | 'image';
const TRACE_LIMIT = 200;

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

const targetOptionValues = {
  all: 'all',
  known: 'known',
  unknown: 'unknown',
  whitelist: 'whitelist',
  blacklist: 'blacklist',
  protect: 'protect',
  undefined: 'undefined'
} as const;

const genderOptionValues = {
  all: 'all',
  male: 'male',
  female: 'female'
} as const;

const maskOptionValues = {
  all: 'all',
  masked: 'true',
  unmasked: 'false'
} as const;

const resolveFaceImage = (value?: string | null) => {
  if (!value) {
    return null;
  }
  return value.startsWith('data:image') ? value : `data:image/jpeg;base64,${value}`;
};

const formatNumber = (value?: number | null) => {
  if (value === null || value === undefined) {
    return '-';
  }
  return value.toFixed(3);
};

const formatPersonName = (person?: PersonResponse | null) => {
  if (!person) {
    return '-';
  }
  const parts = [person.firstName, person.lastName].filter(Boolean);
  const name = parts.join(' ').trim();
  if (name) {
    return person.code ? `${name} (${person.code})` : name;
  }
  return person.code || '-';
};

const formatEventPersonName = (person?: FaceEventRecord['person'] | null, personId?: string | null) => {
  if (!person) {
    return personId || '-';
  }
  const parts = [person.firstName, person.lastName].filter(Boolean);
  const name = parts.join(' ').trim();
  if (name) {
    return person.code ? `${name} (${person.code})` : name;
  }
  return person.code || personId || '-';
};

const toUtcIso = (value?: string) => {
  if (!value) {
    return undefined;
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return undefined;
  }
  return date.toISOString();
};

const TWO_HOURS_MS = 2 * 60 * 60 * 1000;

const toLocalInputValue = (date: Date) => {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(
    date.getHours()
  )}:${pad(date.getMinutes())}`;
};

const getDefaultRange = () => {
  const now = new Date();
  return {
    from: toLocalInputValue(new Date(now.getTime() - TWO_HOURS_MS)),
    to: toLocalInputValue(now)
  };
};

function FaceTraceCard({
  event,
  cameraLabel,
  onTrace,
  onCreatePerson,
  onViewMap
}: {
  event: FaceEventRecord;
  cameraLabel?: string;
  onTrace: (eventId: string) => void;
  onCreatePerson?: (event: FaceEventRecord) => void;
  onViewMap?: (event: FaceEventRecord) => void;
}) {
  const { t } = useI18n();
  const faceImage = resolveFaceImage(event.bestshotBase64);
  const cameraDisplay = cameraLabel ?? event.cameraId;
  const showCreateButton = Boolean(onCreatePerson) && !event.isKnown;
  const details = [
    { label: t('common.fields.time'), value: formatDateTime(event.eventTimeUtc, t) },
    { label: t('common.fields.camera'), value: cameraDisplay },
    { label: t('common.fields.cameraIp'), value: event.cameraIp ?? '-' },
    { label: t('common.fields.zone'), value: event.cameraZone ?? '-' },
    { label: t('common.fields.age'), value: event.age ?? '-' },
    { label: t('common.fields.gender'), value: event.gender ?? '-' },
    { label: t('common.fields.mask'), value: event.mask ?? '-' },
    { label: t('common.fields.score'), value: formatNumber(event.score) },
    { label: t('common.fields.similarity'), value: formatNumber(event.similarity) },
    { label: t('common.fields.trace'), value: formatNumber(event.traceSimilarity) },
    { label: t('common.fields.watchlist'), value: event.watchlistEntryId ?? '-' },
    { label: t('common.fields.person'), value: formatEventPersonName(event.person, event.personId) },
    { label: t('common.fields.category'), value: event.person?.category ?? '-' }
  ];

  return (
    <Paper p="md" radius="lg" className="surface-card strong">
      <Group align="flex-start" wrap="nowrap">
        <Stack gap={6} align="center" style={{ width: 96 }}>
          <Box>
            {faceImage ? (
              <Image src={faceImage} w={96} h={96} radius="md" fit="cover" />
            ) : (
              <Box
                w={96}
                h={96}
                className="surface-card"
                style={{ borderRadius: 12, borderStyle: 'dashed' }}
              />
            )}
          </Box>
          {showCreateButton && (
            <Tooltip label={t('pages.faceTrace.actions.createPerson')} position="bottom" withArrow>
              <ActionIcon
                variant="light"
                color="brand"
                onClick={() => onCreatePerson?.(event)}
                aria-label={t('pages.faceTrace.actions.createPerson')}
              >
                <IconUserPlus size={16} />
              </ActionIcon>
            </Tooltip>
          )}
        </Stack>
        <Stack gap="xs" style={{ flex: 1 }}>
          <Group justify="space-between" align="center">
            <Text size="sm" fw={600}>
              {cameraDisplay}
            </Text>
            <Badge color={event.isKnown ? 'brand' : 'orange'} variant="light">
              {event.isKnown ? t('common.status.known') : t('common.status.unknown')}
            </Badge>
          </Group>
          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
            {details.map((detail) => (
              <Group key={detail.label} gap={6} wrap="nowrap">
                <Text size="xs" className="muted-text">
                  {detail.label}:
                </Text>
                <Text size="xs" fw={500} lineClamp={1}>
                  {detail.value}
                </Text>
              </Group>
            ))}
          </SimpleGrid>
          <Group justify="flex-end">
            {onViewMap && (
              <Tooltip label={t('pages.faceTrace.actions.viewOnMap')} position="bottom" withArrow>
                <ActionIcon
                  variant="light"
                  color="brand"
                  onClick={() => onViewMap(event)}
                  aria-label={t('pages.faceTrace.actions.viewOnMap')}
                >
                  <IconMapPin size={16} />
                </ActionIcon>
              </Tooltip>
            )}
            <Button
              variant="light"
              size="xs"
              leftSection={<IconTargetArrow size={14} />}
              onClick={() => onTrace(event.id)}
            >
              {t('pages.faceTrace.actions.traceFromEvent')}
            </Button>
          </Group>
        </Stack>
      </Group>
    </Paper>
  );
}

export function FaceTrace() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const [activeTab, setActiveTab] = useState<'events' | 'map'>('events');
  const [mode, setMode] = useState<TraceMode>('search');
  const [results, setResults] = useState<FaceEventRecord[]>([]);
  const [total, setTotal] = useState(0);
  const [searchLoading, setSearchLoading] = useState(false);
  const [traceLoading, setTraceLoading] = useState(false);
  const [fromLocal, setFromLocal] = useState(() => getDefaultRange().from);
  const [toLocal, setToLocal] = useState(() => getDefaultRange().to);
  const [timeTouched, setTimeTouched] = useState(false);
  const [cameraIds, setCameraIds] = useState<string[]>([]);
  const [allCameras, setAllCameras] = useState(true);
  const [target, setTarget] = useState('all');
  const [gender, setGender] = useState('all');
  const [mask, setMask] = useState('all');
  const [ageMin, setAgeMin] = useState<number | ''>('');
  const [ageMax, setAgeMax] = useState<number | ''>('');
  const [scoreMin, setScoreMin] = useState<number | ''>('');
  const [similarityMin, setSimilarityMin] = useState<number | ''>('');
  const [personQuery, setPersonQuery] = useState('');
  const [category, setCategory] = useState('');
  const [hasFeatureOnly, setHasFeatureOnly] = useState(true);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [traceSimilarityMin, setTraceSimilarityMin] = useState(0.7);
  const [traceSource, setTraceSource] = useState<TraceSource>('person');
  const [selectedPersonId, setSelectedPersonId] = useState<string | null>(null);
  const [traceImage, setTraceImage] = useState<File | null>(null);
  const [traceImageBase64, setTraceImageBase64] = useState<string | null>(null);
  const [traceDetectedFaces, setTraceDetectedFaces] = useState<FaceDetectResponse[]>([]);
  const [traceSelectedFace, setTraceSelectedFace] = useState<FaceDetectResponse | null>(null);
  const [traceDetectingFaces, setTraceDetectingFaces] = useState(false);
  const [traceDetectError, setTraceDetectError] = useState<string | null>(null);
  const [createEnrollEnabled, setCreateEnrollEnabled] = useState(false);
  const [createImagePreview, setCreateImagePreview] = useState<string | null>(null);
  const [createCrop, setCreateCrop] = useState({ x: 0, y: 0 });
  const [createZoom, setCreateZoom] = useState(1);
  const [createCroppedAreaPixels, setCreateCroppedAreaPixels] = useState<Area | null>(null);
  const [createCroppedPreview, setCreateCroppedPreview] = useState<string | null>(null);
  const [createCroppedBase64, setCreateCroppedBase64] = useState<string | null>(null);
  const [createCropError, setCreateCropError] = useState<string | null>(null);
  const [commonFiltersOpen, setCommonFiltersOpen] = useState(true);
  const [searchFiltersOpen, setSearchFiltersOpen] = useState(true);
  const [traceActionsOpen, setTraceActionsOpen] = useState(true);

  const [createPersonOpened, createPersonModal] = useDisclosure(false);

  const camerasQuery = useQuery({
    queryKey: ['cameras'],
    queryFn: listCameras
  });

  const handleViewOnMap = () => {
    setActiveTab('map');
  };

  const personsQuery = useQuery({
    queryKey: ['persons'],
    queryFn: listPersons
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
      return '-';
    }
    return cameraLabelById.get(cameraId) ?? cameraId;
  };

  const personOptions = useMemo(() => {
    return (personsQuery.data ?? []).map((person: PersonResponse) => ({
      value: person.id,
      label: formatPersonName(person)
    }));
  }, [personsQuery.data]);

  const hasCameras = cameraOptions.length > 0;
  const listTypeOptions = useMemo(
    () => [
      { value: '', label: t('pages.faceTrace.form.listType.protect') },
      { value: 'WhiteList', label: t('pages.faceTrace.form.listType.white') },
      { value: 'BlackList', label: t('pages.faceTrace.form.listType.black') }
    ],
    [t]
  );
  const targetOptions = useMemo(
    () => [
      { value: targetOptionValues.all, label: t('pages.faceTrace.filters.targets.all') },
      { value: targetOptionValues.known, label: t('pages.faceTrace.filters.targets.known') },
      { value: targetOptionValues.unknown, label: t('pages.faceTrace.filters.targets.unknown') },
      { value: targetOptionValues.whitelist, label: t('pages.faceTrace.filters.targets.whitelist') },
      { value: targetOptionValues.blacklist, label: t('pages.faceTrace.filters.targets.blacklist') },
      { value: targetOptionValues.protect, label: t('pages.faceTrace.filters.targets.protect') },
      { value: targetOptionValues.undefined, label: t('pages.faceTrace.filters.targets.undefined') }
    ],
    [t]
  );
  const genderOptions = useMemo(
    () => [
      { value: genderOptionValues.all, label: t('pages.faceTrace.filters.gender.all') },
      { value: genderOptionValues.male, label: t('pages.faceTrace.filters.gender.male') },
      { value: genderOptionValues.female, label: t('pages.faceTrace.filters.gender.female') }
    ],
    [t]
  );
  const maskOptions = useMemo(
    () => [
      { value: maskOptionValues.all, label: t('pages.faceTrace.filters.mask.all') },
      { value: maskOptionValues.masked, label: t('pages.faceTrace.filters.mask.masked') },
      { value: maskOptionValues.unmasked, label: t('pages.faceTrace.filters.mask.unmasked') }
    ],
    [t]
  );

  const personForm = useForm<PersonRequest>({
    initialValues: defaultPersonValues,
    validate: {
      code: (value) =>
        value.trim().length === 0 ? t('pages.faceTrace.validation.codeRequired') : null,
      firstName: (value) =>
        value.trim().length === 0 ? t('pages.faceTrace.validation.firstNameRequired') : null,
      lastName: (value) =>
        value.trim().length === 0 ? t('pages.faceTrace.validation.lastNameRequired') : null
    }
  });

  const createEnrollForm = useForm<EnrollFormValues>({
    initialValues: defaultEnrollValues,
    validate: {
      cameraId: (value) =>
        createEnrollEnabled && value.trim().length === 0
          ? t('pages.faceTrace.validation.cameraRequired')
          : null,
      imageFile: (value) =>
        createEnrollEnabled && !value ? t('pages.faceTrace.validation.faceImageRequired') : null
    }
  });

  const resetTraceDetectState = () => {
    setTraceDetectedFaces([]);
    setTraceSelectedFace(null);
    setTraceDetectError(null);
  };

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

  const resetCommonFilters = () => {
    const defaults = getDefaultRange();
    setFromLocal(defaults.from);
    setToLocal(defaults.to);
    setTimeTouched(false);
    setAllCameras(true);
    setCameraIds([]);
  };

  const clearFilters = () => {
    resetCommonFilters();
    setTarget('all');
    setGender('all');
    setMask('all');
    setAgeMin('');
    setAgeMax('');
    setScoreMin('');
    setSimilarityMin('');
    setPersonQuery('');
    setCategory('');
    setHasFeatureOnly(true);
    setPage(1);
    setPageSize(20);
  };

  const clearTraceActions = () => {
    setTraceSimilarityMin(0.7);
    setTraceSource('person');
    setSelectedPersonId(null);
    setTraceImage(null);
    setTraceImageBase64(null);
    resetTraceDetectState();
  };

  const detectTraceFaces = async () => {
    if (!traceImage) {
      setTraceDetectError(t('pages.faceTrace.traceActions.detect.noImage'));
      return;
    }

    setTraceDetectingFaces(true);
    setTraceDetectError(null);
    setTraceDetectedFaces([]);
    setTraceSelectedFace(null);

    try {
      const base64 = await fileToBase64(traceImage);
      const faces = await detectPersonFaces({ imageBase64: base64 });
      setTraceDetectedFaces(faces);
      if (faces.length === 0) {
        setTraceDetectError(t('pages.faceTrace.traceActions.detect.noFace'));
      }
    } catch (error) {
      const message = (error as Error)?.message ?? t('pages.faceTrace.traceActions.detect.failed');
      setTraceDetectError(message);
    } finally {
      setTraceDetectingFaces(false);
    }
  };

  const selectTraceDetectedFace = (face: FaceDetectResponse) => {
    setTraceSelectedFace(face);
  };
  const onCreateCropComplete = (_: Area, areaPixels: Area) => {
    setCreateCroppedAreaPixels(areaPixels);
  };
  useEffect(() => {
    resetTraceDetectState();
    if (!traceImage) {
      setTraceImageBase64(null);
      return;
    }

    fileToBase64(traceImage)
      .then(setTraceImageBase64)
      .catch(() => setTraceImageBase64(null));
  }, [traceImage]);

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

  const ensureDefaultTimeRange = () => {
    if (timeTouched) {
      return { from: fromLocal, to: toLocal };
    }
    const defaults = getDefaultRange();
    setFromLocal(defaults.from);
    setToLocal(defaults.to);
    return defaults;
  };

  const buildCommonFilter = () => {
    const range = ensureDefaultTimeRange();
    return {
      fromUtc: toUtcIso(range.from),
      toUtc: toUtcIso(range.to),
      cameraIds: !allCameras && cameraIds.length > 0 ? cameraIds : undefined
    };
  };

  const buildFilter = (): FaceEventSearchFilter => {
    const common = buildCommonFilter();
    const filter: FaceEventSearchFilter = {
      ...common,
      isKnown: undefined,
      listType: undefined,
      gender: gender === 'all' ? undefined : gender,
      mask: mask === 'all' ? undefined : mask,
      ageMin: typeof ageMin === 'number' ? ageMin : undefined,
      ageMax: typeof ageMax === 'number' ? ageMax : undefined,
      scoreMin: typeof scoreMin === 'number' ? scoreMin : undefined,
      similarityMin: typeof similarityMin === 'number' ? similarityMin : undefined,
      personQuery: personQuery.trim() || undefined,
      category: category.trim() || undefined,
      hasFeature: hasFeatureOnly ? true : undefined,
      watchlistEntryIds: undefined
    };

    switch (target) {
      case 'known':
        filter.isKnown = true;
        break;
      case 'unknown':
        filter.isKnown = false;
        break;
      case 'whitelist':
        filter.listType = 'WhiteList';
        break;
      case 'blacklist':
        filter.listType = 'BlackList';
        break;
      case 'protect':
        filter.listType = 'Protect';
        break;
      case 'undefined':
        filter.listType = 'Undefined';
        break;
      default:
        break;
    }

    return filter;
  };

  const buildTraceFilter = (): FaceEventSearchFilter => {
    const common = buildCommonFilter();
    return { ...common, hasFeature: true };
  };

  const runSearch = async (nextPage?: number) => {
    const activePage = nextPage ?? page;
    setSearchLoading(true);
    try {
      const payload: FaceEventSearchRequest = {
        ...buildFilter(),
        page: activePage,
        pageSize,
        includeBestshot: true
      };
      const response = await searchFaceEvents(payload);
      setResults(response.items);
      setTotal(response.total);
      setMode('search');
      setPage(activePage);
    } catch (error) {
      notifications.show({
        title: t('pages.faceTrace.notifications.searchFailed.title'),
        message:
          error instanceof Error
            ? error.message
            : t('pages.faceTrace.notifications.searchFailed.message'),
        color: 'red'
      });
    } finally {
      setSearchLoading(false);
    }
  };

  const runTraceByPerson = async () => {
    if (!selectedPersonId) {
      notifications.show({
        title: t('pages.faceTrace.notifications.selectPerson.title'),
        message: t('pages.faceTrace.notifications.selectPerson.message'),
        color: 'yellow'
      });
      return;
    }

    setTraceLoading(true);
    try {
      const response = await traceFaceByPerson({
        personId: selectedPersonId,
        topK: TRACE_LIMIT,
        similarityMin: traceSimilarityMin,
        includeBestshot: true,
        filter: buildTraceFilter()
      });
      setResults(response.items);
      setTotal(response.total);
      setMode('trace');
    } catch (error) {
      notifications.show({
        title: t('pages.faceTrace.notifications.traceFailed.title'),
        message:
          error instanceof Error
            ? error.message
            : t('pages.faceTrace.notifications.traceFailed.person'),
        color: 'red'
      });
    } finally {
      setTraceLoading(false);
    }
  };

  const runTraceByImage = async () => {
    const base64 = traceSelectedFace?.thumbnailBase64 ?? traceImageBase64;
    if (!base64) {
      notifications.show({
        title: t('pages.faceTrace.notifications.uploadImage.title'),
        message: t('pages.faceTrace.notifications.uploadImage.message'),
        color: 'yellow'
      });
      return;
    }

    setTraceLoading(true);
    try {
      const response = await traceFaceByImage({
        imageBase64: base64,
        topK: TRACE_LIMIT,
        similarityMin: traceSimilarityMin,
        includeBestshot: true,
        filter: buildTraceFilter()
      });
      setResults(response.items);
      setTotal(response.total);
      setMode('trace');
    } catch (error) {
      notifications.show({
        title: t('pages.faceTrace.notifications.traceFailed.title'),
        message:
          error instanceof Error
            ? error.message
            : t('pages.faceTrace.notifications.traceFailed.image'),
        color: 'red'
      });
    } finally {
      setTraceLoading(false);
    }
  };

  const runTraceByEvent = async (eventId: string) => {
    setTraceLoading(true);
    try {
      const response = await traceFaceByEvent(eventId, {
        topK: TRACE_LIMIT,
        similarityMin: traceSimilarityMin,
        includeBestshot: true,
        filter: buildTraceFilter()
      });
      setResults(response.items);
      setTotal(response.total);
      setMode('trace');
    } catch (error) {
      notifications.show({
        title: t('pages.faceTrace.notifications.traceFailed.title'),
        message:
          error instanceof Error
            ? error.message
            : t('pages.faceTrace.notifications.traceFailed.event'),
        color: 'red'
      });
    } finally {
      setTraceLoading(false);
    }
  };

  const onImageChange = (file: File | null) => {
    setTraceImage(file);
  };

      const applyCreateCrop = async () => {
    if (!createImagePreview || !createCroppedAreaPixels) {
      setCreateCropError(t('pages.faceTrace.crop.selectArea'));
      return;
    }

    try {
      const dataUrl = await getCroppedImageDataUrl(createImagePreview, createCroppedAreaPixels);
      setCreateCroppedPreview(dataUrl);
      setCreateCroppedBase64(dataUrl);
      setCreateCropError(null);
    } catch (error) {
      setCreateCropError(t('pages.faceTrace.crop.failed'));
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
          result.enrollError?.message ?? t('pages.faceTrace.notifications.enrollFailed.message');
        notifications.show({
          title: t('pages.faceTrace.notifications.personCreated.title'),
          message: t('pages.faceTrace.notifications.personCreated.retry', { message }),
          color: 'yellow'
        });
      } else {
        notifications.show({
          title: result.enrolled
            ? t('pages.faceTrace.notifications.personCreatedEnrolled.title')
            : t('pages.faceTrace.notifications.personCreated.title'),
          message: result.enrolled
            ? t('pages.faceTrace.notifications.personCreatedEnrolled.message')
            : t('pages.faceTrace.notifications.personCreated.message'),
          color: 'brand'
        });
      }
      closeCreateModal();
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.faceTrace.notifications.createFailed.title'),
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

  const openCreateFromEvent = (event: FaceEventRecord) => {
    personForm.setValues(defaultPersonValues);
    personForm.resetDirty();
    personForm.clearErrors();
    resetCreateEnrollState();

    const fallbackCamera = camerasQuery.data?.[0]?.cameraId ?? '';
    const cameraId = event.cameraId || fallbackCamera;
    const faceDataUrl = resolveFaceImage(event.bestshotBase64);
    let faceFile: File | null = null;

    if (faceDataUrl && hasCameras) {
      try {
        faceFile = dataUrlToFile(faceDataUrl, `trace-face-${event.id}.jpg`);
      } catch (error) {
        notifications.show({
          title: t('pages.faceTrace.notifications.faceImageError.title'),
          message:
            (error as Error).message ??
            t('pages.faceTrace.notifications.faceImageError.message'),
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
          t('pages.faceTrace.validation.faceImageRequired')
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
          title: t('pages.faceTrace.notifications.enrollFailed.title'),
          message:
            (error as Error).message ?? t('pages.faceTrace.notifications.enrollFailed.message'),
          color: 'red'
        });
        return;
      }
    }

    createMutation.mutate({ person: normalized, enrollPayload });
  };

  const traceBadge =
    mode === 'search' ? t('pages.faceTrace.badges.search') : t('pages.faceTrace.badges.trace');
  const isLoading = searchLoading || traceLoading;

  return (
    <Stack gap="lg" className="page face-trace-shell">
      <Group justify="space-between" align="center">
        <Stack gap={4}>
          <Text size="sm" className="muted-text">
            {t('pages.faceTrace.subtitle')}
          </Text>
          <Text size="xl" fw={600}>
            {t('pages.faceTrace.title')}
          </Text>
        </Stack>
        <Badge color={mode === 'search' ? 'blue' : 'brand'} variant="light">
          {traceBadge}
        </Badge>
      </Group>

      <Tabs
        value={activeTab}
        onChange={(value) => value && setActiveTab(value as 'events' | 'map')}
        variant="pills"
        className="face-trace-tabs"
      >
        <Tabs.List>
          <Tabs.Tab value="events" leftSection={<IconRadar size={16} />}>
            {t('pages.faceTrace.tabs.events')}
          </Tabs.Tab>
          <Tabs.Tab value="map" leftSection={<IconMapPin size={16} />}>
            {t('pages.faceTrace.tabs.map')}
          </Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="events" pt="md" className="face-trace-panel">
          <Grid gutter="lg" className="face-trace-grid">
            <Grid.Col span={{ base: 12, lg: 4 }} className="face-trace-column">
              <ScrollArea type="auto" h="100%" offsetScrollbars>
                <Stack gap="lg" pr="xs">
                  <Paper p="lg" radius="lg" className="surface-card">
                    <Stack gap="md">
                      <Group justify="space-between" align="center">
                        <Group gap="xs">
                          <IconClock size={16} />
                          <Text size="sm" fw={600}>
                            {t('pages.faceTrace.filters.timeAndCameras')}
                          </Text>
                        </Group>
                        <ActionIcon
                          variant="light"
                          onClick={() => setCommonFiltersOpen((opened) => !opened)}
                          aria-label={t('pages.faceTrace.filters.toggleTimeAndCameras')}
                        >
                          {commonFiltersOpen ? (
                            <IconChevronUp size={16} />
                          ) : (
                            <IconChevronDown size={16} />
                          )}
                        </ActionIcon>
                      </Group>
                      {commonFiltersOpen && (
                        <>
                          <TextInput
                            type="datetime-local"
                            label={t('pages.faceTrace.filters.from')}
                            value={fromLocal}
                            onChange={(event) => {
                              setFromLocal(event.currentTarget.value);
                              setTimeTouched(true);
                            }}
                          />
                          <TextInput
                            type="datetime-local"
                            label={t('pages.faceTrace.filters.to')}
                            value={toLocal}
                            onChange={(event) => {
                              setToLocal(event.currentTarget.value);
                              setTimeTouched(true);
                            }}
                          />
                          <Switch
                            label={t('pages.faceTrace.filters.allCameras')}
                            checked={allCameras}
                            onChange={(event) => {
                              const checked = event.currentTarget.checked;
                              setAllCameras(checked);
                              if (checked) {
                                setCameraIds([]);
                              }
                            }}
                          />
                          <MultiSelect
                            label={t('pages.faceTrace.filters.cameras')}
                            placeholder={
                              allCameras
                                ? t('pages.faceTrace.filters.allCameras')
                                : t('pages.faceTrace.filters.selectCameras')
                            }
                            data={cameraOptions}
                            value={cameraIds}
                            onChange={(value) => {
                              setCameraIds(value);
                              if (value.length > 0) {
                                setAllCameras(false);
                              }
                            }}
                            searchable
                            clearable
                            disabled={allCameras}
                          />
                        </>
                      )}
                    </Stack>
                  </Paper>

                  <Paper p="lg" radius="lg" className="surface-card">
                    <Stack gap="md">
                      <Group justify="space-between" align="center">
                        <Group gap="xs">
                          <IconFilter size={16} />
                          <Text size="sm" fw={600}>
                            {t('pages.faceTrace.filters.searchTitle')}
                          </Text>
                        </Group>
                        <ActionIcon
                          variant="light"
                          onClick={() => setSearchFiltersOpen((opened) => !opened)}
                          aria-label={t('pages.faceTrace.filters.toggleSearch')}
                        >
                          {searchFiltersOpen ? (
                            <IconChevronUp size={16} />
                          ) : (
                            <IconChevronDown size={16} />
                          )}
                        </ActionIcon>
                      </Group>
                      {searchFiltersOpen && (
                        <>
                          <Select
                            label={t('pages.faceTrace.filters.target')}
                            data={targetOptions}
                            value={target}
                            onChange={(value) => setTarget(value ?? 'all')}
                          />
                          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
                            <Select
                              label={t('pages.faceTrace.filters.gender.label')}
                              data={genderOptions}
                              value={gender}
                              onChange={(value) => setGender(value ?? 'all')}
                            />
                            <Select
                              label={t('pages.faceTrace.filters.mask.label')}
                              data={maskOptions}
                              value={mask}
                              onChange={(value) => setMask(value ?? 'all')}
                            />
                          </SimpleGrid>
                          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
                            <NumberInput
                              label={t('pages.faceTrace.filters.ageMin')}
                              value={ageMin}
                              onChange={(value) =>
                                setAgeMin(typeof value === 'number' ? value : '')
                              }
                              min={0}
                            />
                            <NumberInput
                              label={t('pages.faceTrace.filters.ageMax')}
                              value={ageMax}
                              onChange={(value) =>
                                setAgeMax(typeof value === 'number' ? value : '')
                              }
                              min={0}
                            />
                          </SimpleGrid>
                          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
                            <NumberInput
                              label={t('pages.faceTrace.filters.scoreMin')}
                              value={scoreMin}
                              onChange={(value) =>
                                setScoreMin(typeof value === 'number' ? value : '')
                              }
                              min={0}
                              max={1}
                              step={0.01}
                              decimalScale={2}
                            />
                            <NumberInput
                              label={t('pages.faceTrace.filters.similarityMin')}
                              value={similarityMin}
                              onChange={(value) =>
                                setSimilarityMin(typeof value === 'number' ? value : '')
                              }
                              min={0}
                              max={1}
                              step={0.01}
                              decimalScale={2}
                            />
                          </SimpleGrid>
                          <TextInput
                            label={t('pages.faceTrace.filters.personSearch')}
                            placeholder={t('pages.faceTrace.filters.personSearchPlaceholder')}
                            value={personQuery}
                            onChange={(event) => setPersonQuery(event.currentTarget.value)}
                          />
                          <TextInput
                            label={t('pages.faceTrace.filters.category')}
                            placeholder={t('pages.faceTrace.filters.categoryPlaceholder')}
                            value={category}
                            onChange={(event) => setCategory(event.currentTarget.value)}
                          />
                          <Switch
                            label={t('pages.faceTrace.filters.hasFeature')}
                            checked={hasFeatureOnly}
                            onChange={(event) => setHasFeatureOnly(event.currentTarget.checked)}
                          />
                          <Divider />
                          <Stack gap="xs">
                            <NumberInput
                              label={t('pages.faceTrace.filters.pageSize')}
                              value={pageSize}
                              onChange={(value) =>
                                setPageSize(typeof value === 'number' ? value : 20)
                              }
                              min={5}
                              max={200}
                              step={5}
                            />
                            <Group justify="space-between" align="center">
                              <Button
                                variant="light"
                                color="brand"
                                leftSection={<IconRefresh size={16} />}
                                onClick={clearFilters}
                                disabled={isLoading}
                              >
                                {t('common.actions.clearSelection')}
                              </Button>
                              <Button
                                variant="filled"
                                color="brand"
                                leftSection={<IconSearch size={16} />}
                                onClick={() => runSearch(1)}
                                loading={searchLoading}
                              >
                                {t('pages.faceTrace.actions.search')}
                              </Button>
                            </Group>
                          </Stack>
                        </>
                      )}
                    </Stack>
                  </Paper>

                  <Paper p="lg" radius="lg" className="surface-card">
                    <Stack gap="md">
                      <Group justify="space-between" align="center">
                        <Group gap="xs">
                          <IconRadar size={16} />
                          <Text size="sm" fw={600}>
                            {t('pages.faceTrace.traceActions.title')}
                          </Text>
                        </Group>
                        <ActionIcon
                          variant="light"
                          onClick={() => setTraceActionsOpen((opened) => !opened)}
                          aria-label={t('pages.faceTrace.traceActions.toggle')}
                        >
                          {traceActionsOpen ? (
                            <IconChevronUp size={16} />
                          ) : (
                            <IconChevronDown size={16} />
                          )}
                        </ActionIcon>
                      </Group>
                      {traceActionsOpen && (
                        <>
                          <NumberInput
                            label={t('pages.faceTrace.traceActions.similarity.label')}
                            value={traceSimilarityMin}
                            onChange={(value) =>
                              setTraceSimilarityMin(typeof value === 'number' ? value : 0.4)
                            }
                            min={0}
                            max={1}
                            step={0.01}
                            decimalScale={2}
                            description={t('pages.faceTrace.traceActions.similarity.desc')}
                          />
                          <Radio.Group
                            label={t('pages.faceTrace.traceActions.source.label')}
                            value={traceSource}
                            onChange={(value) => setTraceSource(value as TraceSource)}
                          >
                            <Group mt="xs">
                              <Radio value="person" label={t('pages.faceTrace.traceActions.source.person')} />
                              <Radio value="image" label={t('pages.faceTrace.traceActions.source.image')} />
                            </Group>
                          </Radio.Group>
                          {traceSource === 'person' ? (
                            <>
                              <Select
                                label={t('pages.faceTrace.traceActions.person.label')}
                                placeholder={t('pages.faceTrace.traceActions.person.placeholder')}
                                data={personOptions}
                                value={selectedPersonId}
                                onChange={setSelectedPersonId}
                                searchable
                                clearable
                              />
                            </>
                          ) : (
                            <>
                              <FileInput
                                label={t('pages.faceTrace.traceActions.image.label')}
                                placeholder={t('pages.faceTrace.traceActions.image.placeholder')}
                                value={traceImage}
                                onChange={onImageChange}
                                accept="image/*"
                                leftSection={<IconUpload size={16} />}
                              />
                              {traceImage && (
                                <Stack gap="sm">
                                  <Group justify="space-between" align="center">
                                    <Button
                                      size="xs"
                                      variant="light"
                                      leftSection={<IconSearch size={14} />}
                                      loading={traceDetectingFaces}
                                      onClick={detectTraceFaces}
                                    >
                                      {t('pages.faceTrace.traceActions.detect.action')}
                                    </Button>
                                    {traceSelectedFace && (
                                      <Badge color="brand" variant="light">
                                        {t('pages.faceTrace.traceActions.detect.selected')}
                                      </Badge>
                                    )}
                                  </Group>
                                  {traceDetectError && (
                                    <Text size="sm" c="red">
                                      {traceDetectError}
                                    </Text>
                                  )}
                                  {traceDetectedFaces.length > 0 && (
                                    <Stack gap="xs">
                                      <Text size="xs" className="muted-text">
                                        {t('pages.faceTrace.traceActions.detect.results')}
                                      </Text>
                                      <Group gap="sm" wrap="wrap">
                                        {traceDetectedFaces.map((face) => (
                                          <Paper
                                            key={face.faceId}
                                            p={4}
                                            radius="md"
                                            className="surface-card strong"
                                            style={{
                                              cursor: 'pointer',
                                              border:
                                                traceSelectedFace?.faceId === face.faceId
                                                  ? '2px solid var(--mantine-color-brand-6)'
                                                  : '1px solid var(--mantine-color-gray-4)'
                                            }}
                                            onClick={() => selectTraceDetectedFace(face)}
                                          >
                                            <img
                                              src={face.thumbnailBase64}
                                              alt={t('pages.faceTrace.traceActions.detect.faceAlt')}
                                              style={{ width: 72, height: 72, objectFit: 'cover' }}
                                            />
                                            <Text size="xs" ta="center">
                                              {face.score.toFixed(2)}
                                            </Text>
                                          </Paper>
                                        ))}
                                      </Group>
                                    </Stack>
                                  )}
                                </Stack>
                              )}
                            </>
                          )}
                          <Group justify="space-between" align="center">
                            <Button
                              variant="light"
                              color="brand"
                              leftSection={<IconRefresh size={16} />}
                              onClick={clearTraceActions}
                              disabled={isLoading}
                            >
                              {t('common.actions.clearSelection')}
                            </Button>
                            <Button
                              variant="filled"
                              color="brand"
                              leftSection={
                                traceSource === 'person' ? (
                                  <IconTargetArrow size={16} />
                                ) : (
                                  <IconUpload size={16} />
                                )
                              }
                              onClick={traceSource === 'person' ? runTraceByPerson : runTraceByImage}
                              loading={traceLoading}
                            >
                              {traceSource === 'person'
                                ? t('pages.faceTrace.traceActions.run.person')
                                : t('pages.faceTrace.traceActions.run.image')}
                            </Button>
                          </Group>
                        </>
                      )}
                    </Stack>
                  </Paper>
                </Stack>
              </ScrollArea>
            </Grid.Col>

            <Grid.Col span={{ base: 12, lg: 8 }} className="face-trace-column">
              <Stack gap="lg" className="face-trace-results">
                <Paper p="lg" radius="lg" className="surface-card">
                  <Group justify="space-between" align="center">
                    <Stack gap={4}>
                      <Text size="sm" className="muted-text">
                        {mode === 'search'
                          ? t('pages.faceTrace.results.subtitle.search')
                          : t('pages.faceTrace.results.subtitle.trace')}
                      </Text>
                      <Text size="lg" fw={600}>
                        {t('pages.faceTrace.results.title')}
                      </Text>
                    </Stack>
                    <Group gap="sm">
                      <Badge color="brand" variant="light">
                        {total}
                      </Badge>
                      {isLoading && (
                        <Badge color="yellow" variant="light">
                          {t('pages.faceTrace.results.loading')}
                        </Badge>
                      )}
                    </Group>
                  </Group>
                </Paper>

                <ScrollArea type="auto" className="face-trace-scroll">
                  <Stack gap="lg">
                    {results.length === 0 ? (
                      <Paper p="lg" radius="lg" className="surface-card">
                        <Text size="sm" className="muted-text">
                          {t('pages.faceTrace.results.empty')}
                        </Text>
                      </Paper>
                    ) : (
                      <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="lg">
                        {results.map((event) => (
                          <FaceTraceCard
                            key={event.id}
                            event={event}
                            cameraLabel={resolveCameraLabel(event.cameraId)}
                            onTrace={runTraceByEvent}
                            onCreatePerson={openCreateFromEvent}
                            onViewMap={mode === 'trace' ? handleViewOnMap : undefined}
                          />
                        ))}
                      </SimpleGrid>
                    )}

                    {mode === 'search' && total > pageSize && (
                      <Group justify="center">
                        <Pagination
                          value={page}
                          onChange={(value) => runSearch(value)}
                          total={Math.max(1, Math.ceil(total / pageSize))}
                          radius="xl"
                        />
                      </Group>
                    )}
                  </Stack>
                </ScrollArea>
              </Stack>
            </Grid.Col>
          </Grid>
        </Tabs.Panel>

        <Tabs.Panel value="map" pt="md" className="face-trace-panel">
          <FaceTraceMapPanel results={results} />
        </Tabs.Panel>
      </Tabs>
      <Modal
        opened={createPersonOpened}
        onClose={closeCreateModal}
        title={t('pages.faceTrace.modals.addPerson.title')}
        size="lg"
      >
        <form onSubmit={personForm.onSubmit(onCreateSubmit)}>
          <Stack gap="md">
            <Group grow>
              <TextInput
                label={t('pages.faceTrace.form.code')}
                placeholder={t('pages.faceTrace.form.placeholders.code')}
                {...personForm.getInputProps('code')}
              />
              <TextInput
                label={t('pages.faceTrace.form.category')}
                placeholder={t('pages.faceTrace.form.placeholders.category')}
                {...personForm.getInputProps('category')}
              />
            </Group>
            <Select
              label={t('pages.faceTrace.form.listType.label')}
              placeholder={t('pages.faceTrace.form.listType.placeholder')}
              data={listTypeOptions}
              value={personForm.values.listType ?? ''}
              onChange={(value) => personForm.setFieldValue('listType', value ?? '')}
            />
            <Group grow>
              <TextInput
                label={t('pages.faceTrace.form.firstName')}
                placeholder={t('pages.faceTrace.form.placeholders.firstName')}
                {...personForm.getInputProps('firstName')}
              />
              <TextInput
                label={t('pages.faceTrace.form.lastName')}
                placeholder={t('pages.faceTrace.form.placeholders.lastName')}
                {...personForm.getInputProps('lastName')}
              />
            </Group>
            <Group grow>
              <TextInput
                label={t('pages.faceTrace.form.gender')}
                placeholder={t('pages.faceTrace.form.placeholders.gender')}
                {...personForm.getInputProps('gender')}
              />
              <NumberInput
                label={t('pages.faceTrace.form.age')}
                placeholder={t('pages.faceTrace.form.placeholders.age')}
                min={0}
                max={120}
                {...personForm.getInputProps('age')}
              />
            </Group>
            <Textarea
              label={t('pages.faceTrace.form.remarks')}
              placeholder={t('pages.faceTrace.form.placeholders.remarks')}
              autosize
              minRows={2}
              {...personForm.getInputProps('remarks')}
            />
            <Paper p="md" radius="lg" className="surface-card strong">
              <Stack gap="sm">
                <Group justify="space-between" align="center">
                  <Stack gap={2}>
                    <Text size="sm" fw={600}>
                      {t('pages.faceTrace.enroll.title')}
                    </Text>
                    <Text size="xs" className="muted-text">
                      {t('pages.faceTrace.enroll.subtitle')}
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
                    {t('pages.faceTrace.enroll.noCamera')}
                  </Text>
                )}
                {createEnrollEnabled && (
                  <Stack gap="sm">
                    <Select
                      label={t('pages.faceTrace.enroll.camera')}
                      placeholder={t('pages.faceTrace.enroll.cameraPlaceholder')}
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
                      nothingFoundMessage={t('pages.faceTrace.enroll.noCameras')}
                    />
                    <FileInput
                      label={t('pages.faceTrace.enroll.faceImage')}
                      placeholder={t('pages.faceTrace.enroll.faceImagePlaceholder')}
                      accept="image/*"
                      clearable
                      value={createEnrollForm.values.imageFile}
                      onChange={(file) => createEnrollForm.setFieldValue('imageFile', file)}
                      error={createEnrollForm.errors.imageFile}
                    />
                    {createImagePreview && (
                      <Stack gap="sm">
                        <Divider
                          label={t('pages.faceTrace.enroll.cropTitle')}
                          labelPosition="center"
                        />
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
                            {t('pages.faceTrace.enroll.zoom')}
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
                              {t('pages.faceTrace.enroll.useOriginal')}
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
                              alt={t('pages.faceTrace.enroll.croppedAlt')}
                              style={{ width: '100%', maxHeight: 220, objectFit: 'contain' }}
                            />
                          </Paper>
                        )}
                      </Stack>
                    )}
                    <TextInput
                      label={t('pages.faceTrace.enroll.sourceCamera')}
                      placeholder={t('pages.faceTrace.enroll.sourceCameraPlaceholder')}
                      value={createEnrollForm.values.sourceCameraId}
                      onChange={(event) =>
                        createEnrollForm.setFieldValue(
                          'sourceCameraId',
                          event.currentTarget.value
                        )
                      }
                    />
                    <Switch
                      label={t('pages.faceTrace.enroll.storeFaceImage')}
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












