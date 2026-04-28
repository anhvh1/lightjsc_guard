import {
  ActionIcon,
  Badge,
  Box,
  Button,
  FileInput,
  Group,
  Modal,
  NumberInput,
  Pagination,
  Paper,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Textarea,
  Tooltip
} from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import {
  IconCheck,
  IconEdit,
  IconListDetails,
  IconPhoto,
  IconQrcode,
  IconSearch,
  IconPlus,
  IconTrash,
  IconX
} from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import { useForm } from '@mantine/form';
import {
  createPerson,
  deletePerson,
  deletePersonTemplate,
  updatePersonTemplateStatus,
  detectPersonFaces,
  enrollPerson,
  listCameras,
  listPersonTemplates,
  listPersons,
  updatePerson
} from '../api/ingestor';
import type {
  CameraResponse,
  EnrollFaceRequest,
  FaceDetectResponse,
  FaceTemplateResponse,
  PersonRequest,
  PersonResponse
} from '../api/types';
import { PersonScanModal, type PersonScanUsePayload } from '../components/persons/PersonScanModal';
import { useI18n } from '../i18n/I18nProvider';
import { formatDateTime } from '../utils/format';
import { dataUrlToFile, fileToBase64 } from '../utils/personScan';

const defaultPersonValues: PersonRequest = {
  code: '',
  firstName: '',
  lastName: '',
  personalId: '',
  documentNumber: '',
  dateOfBirth: null,
  dateOfIssue: null,
  address: '',
  rawQrPayload: '',
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
};

const defaultEnrollValues: EnrollFormValues = {
  cameraId: '',
  imageFile: null
};

const joinPersonName = (firstName?: string | null, lastName?: string | null) =>
  [firstName?.trim(), lastName?.trim()].filter(Boolean).join(' ');

const splitPersonName = (fullName?: string | null) => {
  const normalized = fullName?.trim() ?? '';
  if (!normalized) {
    return { firstName: '', lastName: '' };
  }

  const tokens = normalized.split(/\s+/).filter(Boolean);
  if (tokens.length === 1) {
    return { firstName: tokens[0], lastName: '' };
  }

  return {
    firstName: tokens.slice(0, -1).join(' '),
    lastName: tokens[tokens.length - 1]
  };
};

export function Persons() {
  const queryClient = useQueryClient();
  const { t } = useI18n();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState('20');
  const [editing, setEditing] = useState<PersonResponse | null>(null);
  const [selectedPerson, setSelectedPerson] = useState<PersonResponse | null>(null);
  const [templatePerson, setTemplatePerson] = useState<PersonResponse | null>(null);
  const [createEnrollEnabled, setCreateEnrollEnabled] = useState(false);
  const [detectedFaces, setDetectedFaces] = useState<FaceDetectResponse[]>([]);
  const [selectedDetectedFace, setSelectedDetectedFace] = useState<FaceDetectResponse | null>(null);
  const [detectingFaces, setDetectingFaces] = useState(false);
  const [detectError, setDetectError] = useState<string | null>(null);
  const [createDetectedFaces, setCreateDetectedFaces] = useState<FaceDetectResponse[]>([]);
  const [createSelectedDetectedFace, setCreateSelectedDetectedFace] =
    useState<FaceDetectResponse | null>(null);
  const [createDetectingFaces, setCreateDetectingFaces] = useState(false);
  const [createDetectError, setCreateDetectError] = useState<string | null>(null);

  const listTypeOptions = useMemo(
    () => [
      { value: '', label: t('pages.persons.listType.protectOption') },
      { value: 'WhiteList', label: t('pages.persons.listType.white') },
      { value: 'BlackList', label: t('pages.persons.listType.black') }
    ],
    [t]
  );

  const pageSizeOptions = useMemo(
    () => [
      { value: '10', label: t('common.pagination.perPage', { count: 10 }) },
      { value: '20', label: t('common.pagination.perPage', { count: 20 }) },
      { value: '50', label: t('common.pagination.perPage', { count: 50 }) },
      { value: '100', label: t('common.pagination.perPage', { count: 100 }) }
    ],
    [t]
  );

  const getListTypeMeta = (value?: string | null) => {
    if (!value) {
      return { label: t('pages.persons.listType.protect'), color: 'blue' };
    }

    const normalized = value.toLowerCase();
    if (normalized === 'whitelist') {
      return { label: t('pages.persons.listType.white'), color: 'brand' };
    }

    if (normalized === 'blacklist') {
      return { label: t('pages.persons.listType.black'), color: 'red' };
    }

    return { label: value, color: 'gray' };
  };

  const [personOpened, personModal] = useDisclosure(false);
  const [enrollOpened, enrollModal] = useDisclosure(false);
  const [templateOpened, templateModal] = useDisclosure(false);
  const [scanOpened, scanModal] = useDisclosure(false);

  const personsQuery = useQuery({
    queryKey: ['persons'],
    queryFn: listPersons
  });

  const camerasQuery = useQuery({
    queryKey: ['cameras'],
    queryFn: listCameras
  });

  const templatesQuery = useQuery({
    queryKey: ['persons', templatePerson?.id, 'templates'],
    queryFn: () => listPersonTemplates(templatePerson?.id ?? ''),
    enabled: Boolean(templatePerson?.id && templateOpened)
  });

  const personForm = useForm<PersonRequest>({
    initialValues: defaultPersonValues,
    validate: {
      personalId: (value) =>
        (value ?? '').trim().length === 0
          ? t('pages.persons.validation.personalIdRequired', undefined, 'Số định danh cá nhân là bắt buộc.')
          : null,
      firstName: (value, values) =>
        joinPersonName(value, values.lastName).trim().length === 0
          ? t('pages.persons.validation.fullNameRequired', undefined, 'Họ và tên là bắt buộc.')
          : null
    }
  });

  const enrollForm = useForm<EnrollFormValues>({
    initialValues: defaultEnrollValues,
    validate: {
      cameraId: (value) =>
        value.trim().length === 0 ? t('pages.persons.validation.cameraRequired') : null,
      imageFile: (value) => (!value ? t('pages.persons.validation.faceImageRequired') : null)
    }
  });

  const createEnrollForm = useForm<EnrollFormValues>({
    initialValues: defaultEnrollValues,
    validate: {
      cameraId: (value) =>
        createEnrollEnabled && value.trim().length === 0
          ? t('pages.persons.validation.cameraRequired')
          : null,
      imageFile: (value) =>
        createEnrollEnabled && !value ? t('pages.persons.validation.faceImageRequired') : null
    }
  });

  const resetCropState = () => {
    setDetectedFaces([]);
    setSelectedDetectedFace(null);
    setDetectError(null);
  };

  const resetCreateCropState = () => {
    setCreateDetectedFaces([]);
    setCreateSelectedDetectedFace(null);
    setCreateDetectError(null);
  };

  const resetCreateEnrollState = () => {
    setCreateEnrollEnabled(false);
    createEnrollForm.setValues(defaultEnrollValues);
    createEnrollForm.resetDirty();
    createEnrollForm.clearErrors();
    resetCreateCropState();
  };

  const closeEnroll = () => {
    enrollModal.close();
    enrollForm.reset();
    resetCropState();
    setSelectedPerson(null);
  };

  useEffect(() => {
    resetCropState();
  }, [enrollForm.values.imageFile]);

  useEffect(() => {
    resetCreateCropState();
  }, [createEnrollEnabled, createEnrollForm.values.imageFile]);

  const cameraOptions = useMemo(() => {
    return (camerasQuery.data ?? []).map((camera: CameraResponse) => ({
      value: camera.cameraId,
      label: `${camera.code?.trim() || camera.cameraId}${camera.ipAddress ? ` - ${camera.ipAddress}` : ''}`
    }));
  }, [camerasQuery.data]);

  const hasCameras = cameraOptions.length > 0;
  const personFullName = joinPersonName(personForm.values.firstName, personForm.values.lastName);

  const selectDetectedFace = (face: FaceDetectResponse) => {
    setSelectedDetectedFace(face);
  };

  const selectCreateDetectedFace = (face: FaceDetectResponse) => {
    setCreateSelectedDetectedFace(face);
  };

  const detectFacesFromFile = async (
    file: File | null,
    setFaces: (faces: FaceDetectResponse[]) => void,
    setSelected: (face: FaceDetectResponse | null) => void,
    setError: (value: string | null) => void,
    setLoading: (value: boolean) => void
  ) => {
    if (!file) {
      setError(t('pages.persons.enroll.detect.noImage'));
      return;
    }

    setLoading(true);
    setError(null);
    setFaces([]);
    setSelected(null);

    try {
      const base64 = await fileToBase64(file);
      const faces = await detectPersonFaces({ imageBase64: base64 });
      setFaces(faces);
      if (faces.length === 0) {
        setError(t('pages.persons.enroll.detect.noFace'));
      }
    } catch (error) {
      const message = (error as Error)?.message ?? t('pages.persons.enroll.detect.failed');
      setError(message);
    } finally {
      setLoading(false);
    }
  };

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    if (!term) {
      return personsQuery.data ?? [];
    }

    return (personsQuery.data ?? []).filter((person) => {
      const fullName = `${person.firstName} ${person.lastName}`.toLowerCase();
      const listTypeLabel = getListTypeMeta(person.listType).label.toLowerCase();
      return (
        person.code.toLowerCase().includes(term) ||
        fullName.includes(term) ||
        (person.category ?? '').toLowerCase().includes(term) ||
        listTypeLabel.includes(term)
      );
    });
  }, [personsQuery.data, search]);

  const pageSizeValue = Number(pageSize);
  const totalPages = Math.max(1, Math.ceil(filtered.length / pageSizeValue));
  const paged = useMemo(() => {
    const start = (page - 1) * pageSizeValue;
    return filtered.slice(start, start + pageSizeValue);
  }, [filtered, page, pageSizeValue]);
  const paginationFrom = filtered.length === 0 ? 0 : (page - 1) * pageSizeValue + 1;
  const paginationTo = Math.min(page * pageSizeValue, filtered.length);

  useEffect(() => {
    setPage(1);
  }, [search, pageSizeValue]);

  useEffect(() => {
    if (page > totalPages) {
      setPage(totalPages);
    }
  }, [page, totalPages]);

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
          result.enrollError?.message ?? t('pages.persons.notifications.enrollFailed.message');
        notifications.show({
          title: t('pages.persons.notifications.createdPartial.title'),
          message: t('pages.persons.notifications.createdPartial.message', { message }),
          color: 'yellow'
        });
      } else {
        notifications.show({
          title: result.enrolled
            ? t('pages.persons.notifications.createdEnrolled.title')
            : t('pages.persons.notifications.created.title'),
          message: result.enrolled
            ? t('pages.persons.notifications.createdEnrolled.message')
            : t('pages.persons.notifications.created.message'),
          color: 'brand'
        });
      }
      closePersonModal();
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.persons.notifications.createFailed.title'),
        message: error.message ?? t('pages.persons.notifications.createFailed.message'),
        color: 'red'
      });
    }
  });

  const updateMutation = useMutation({
    mutationFn: (payload: PersonRequest) => {
      if (!editing) {
        return Promise.reject(new Error(t('pages.persons.errors.noPersonSelected')));
      }
      return updatePerson(editing.id, payload);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['persons'] });
      notifications.show({
        title: t('pages.persons.notifications.updated.title'),
        message: t('pages.persons.notifications.updated.message'),
        color: 'brand'
      });
      closePersonModal();
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.persons.notifications.updateFailed.title'),
        message: error.message ?? t('pages.persons.notifications.updateFailed.message'),
        color: 'red'
      });
    }
  });

  const deleteMutation = useMutation({
    mutationFn: deletePerson,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['persons'] });
      notifications.show({
        title: t('pages.persons.notifications.deleted.title'),
        message: t('pages.persons.notifications.deleted.message'),
        color: 'brand'
      });
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.persons.notifications.deleteFailed.title'),
        message: error.message ?? t('pages.persons.notifications.deleteFailed.message'),
        color: 'red'
      });
    }
  });

  const enrollMutation = useMutation({
    mutationFn: async (params: { personId: string; payload: EnrollFaceRequest }) =>
      enrollPerson(params.personId, params.payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['persons'] });
      if (selectedPerson?.id) {
        queryClient.invalidateQueries({ queryKey: ['persons', selectedPerson.id, 'templates'] });
      }
      notifications.show({
        title: t('pages.persons.notifications.enrollSuccess.title'),
        message: t('pages.persons.notifications.enrollSuccess.message'),
        color: 'brand'
      });
      closeEnroll();
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.persons.notifications.enrollFailed.title'),
        message: error.message ?? t('pages.persons.notifications.enrollFailed.message'),
        color: 'red'
      });
    }
  });

  const deleteTemplateMutation = useMutation({
    mutationFn: (payload: { personId: string; templateId: string }) =>
      deletePersonTemplate(payload.personId, payload.templateId),
    onSuccess: () => {
      if (templatePerson?.id) {
        queryClient.invalidateQueries({ queryKey: ['persons', templatePerson.id, 'templates'] });
      }
      notifications.show({
        title: t('pages.persons.notifications.templateRemoved.title'),
        message: t('pages.persons.notifications.templateRemoved.message'),
        color: 'brand'
      });
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.persons.notifications.templateRemoveFailed.title'),
        message: error.message ?? t('pages.persons.notifications.templateRemoveFailed.message'),
        color: 'red'
      });
    }
  });

  const [togglingTemplateId, setTogglingTemplateId] = useState<string | null>(null);
  const toggleTemplateMutation = useMutation({
    mutationFn: (payload: { personId: string; templateId: string; isActive: boolean }) =>
      updatePersonTemplateStatus(payload.personId, payload.templateId, payload.isActive),
    onMutate: (payload) => {
      setTogglingTemplateId(payload.templateId);
    },
    onSuccess: () => {
      if (templatePerson?.id) {
        queryClient.invalidateQueries({ queryKey: ['persons', templatePerson.id, 'templates'] });
      }
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.persons.notifications.templateUpdateFailed.title'),
        message: error.message ?? t('pages.persons.notifications.templateUpdateFailed.message'),
        color: 'red'
      });
    },
    onSettled: () => {
      setTogglingTemplateId(null);
    }
  });

  const isSaving = createMutation.isPending || updateMutation.isPending;

  const openCreate = () => {
    setEditing(null);
    personForm.setValues(defaultPersonValues);
    const firstCamera = camerasQuery.data?.[0]?.cameraId ?? '';
    resetCreateEnrollState();
    createEnrollForm.setValues({
      ...defaultEnrollValues,
      cameraId: firstCamera
    });
    personModal.open();
  };

  const openEdit = (person: PersonResponse) => {
    setEditing(person);
    personForm.setValues({
      code: person.code,
      firstName: person.firstName,
      lastName: person.lastName,
      personalId: person.personalId ?? '',
      documentNumber: person.documentNumber ?? '',
      dateOfBirth: person.dateOfBirth ?? null,
      dateOfIssue: person.dateOfIssue ?? null,
      address: person.address ?? '',
      rawQrPayload: person.rawQrPayload ?? '',
      gender: person.gender ?? '',
      age: person.age ?? null,
      remarks: person.remarks ?? '',
      category: person.category ?? '',
      listType: person.listType ?? '',
      isActive: person.isActive
    });
    personModal.open();
  };

  const closePersonModal = () => {
    personModal.close();
    setEditing(null);
    resetCreateEnrollState();
  };

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
      cameraId: createEnrollForm.values.cameraId || firstCamera
    });
  };

  const confirmDelete = (person: PersonResponse) => {
    modals.openConfirmModal({
      title: t('pages.persons.confirmations.delete.title'),
      children: (
        <Text size="sm">
          {t('pages.persons.confirmations.delete.message', {
            name: `${person.firstName} ${person.lastName}`.trim(),
            code: person.code
          })}
        </Text>
      ),
      labels: { confirm: t('common.actions.delete'), cancel: t('common.actions.cancel') },
      confirmProps: { color: 'red' },
      onConfirm: () => deleteMutation.mutate(person.id)
    });
  };

  const openEnroll = (person: PersonResponse) => {
    setSelectedPerson(person);
    const firstCamera = camerasQuery.data?.[0]?.cameraId ?? '';
    enrollForm.setValues({
      ...defaultEnrollValues,
      cameraId: firstCamera
    });
    resetCropState();
    enrollModal.open();
  };

  const openTemplates = (person: PersonResponse) => {
    setTemplatePerson(person);
    templateModal.open();
  };

  const onSubmit = async (values: PersonRequest) => {
    const personalId = values.personalId?.trim() || '';
    const normalized = {
      ...values,
      code: personalId,
      personalId: personalId || undefined,
      documentNumber: values.documentNumber?.trim() || undefined,
      dateOfBirth: values.dateOfBirth || undefined,
      dateOfIssue: values.dateOfIssue || undefined,
      address: values.address?.trim() || undefined,
      rawQrPayload: values.rawQrPayload?.trim() || undefined,
      gender: values.gender?.trim() || undefined,
      remarks: values.remarks?.trim() || undefined,
      category: values.category?.trim() || undefined,
      listType: values.listType?.trim() || undefined
    };

    if (editing) {
      updateMutation.mutate(normalized);
      return;
    }

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
          t('pages.persons.validation.faceImageRequired')
        );
        return;
      }

      try {
        const base64 =
          createSelectedDetectedFace?.thumbnailBase64 ?? (await fileToBase64(imageFile));
        enrollPayload = {
          cameraId: createEnrollForm.values.cameraId,
          imageBase64: base64,
          storeFaceImage: true
        };
      } catch (error) {
        notifications.show({
          title: t('pages.persons.notifications.enrollFailed.title'),
          message:
            (error as Error).message ?? t('pages.persons.notifications.enrollFailed.message'),
          color: 'red'
        });
        return;
      }
    }

    createMutation.mutate({ person: normalized, enrollPayload });
  };

  const handleUseScannedInForm = async (payload: PersonScanUsePayload) => {
    const personalId = payload.person.personalId?.trim() || '';
    const scannedName = joinPersonName(payload.person.firstName, payload.person.lastName) || payload.person.fullName?.trim() || '';
    const scannedNameParts = splitPersonName(scannedName);
    setEditing(null);
    personForm.setValues({
      ...defaultPersonValues,
      code: personalId,
      firstName: scannedNameParts.firstName,
      lastName: scannedNameParts.lastName,
      personalId,
      documentNumber: payload.person.documentNumber ?? '',
      dateOfBirth: payload.person.dateOfBirth ?? null,
      dateOfIssue: payload.person.dateOfIssue ?? null,
      address: payload.person.address ?? '',
      rawQrPayload: payload.rawQrPayload ?? payload.person.rawQrPayload ?? '',
      gender: payload.person.gender ?? '',
      age: payload.person.age ?? null
    });

    resetCreateEnrollState();
    const firstCamera = payload.systemCameraId ?? camerasQuery.data?.[0]?.cameraId ?? '';
    if (payload.faceImageBase64) {
      createEnrollForm.setValues({
        cameraId: firstCamera,
        imageFile: dataUrlToFile(payload.faceImageBase64, 'scan-face.jpg')
      });
      if (firstCamera) {
        setCreateEnrollEnabled(true);
      }
      setCreateSelectedDetectedFace({
        faceId: 'scan-face',
        score: 1,
        box: { x: 0, y: 0, width: 0, height: 0 },
        thumbnailBase64: payload.faceImageBase64
      });
      setCreateDetectedFaces([
        {
          faceId: 'scan-face',
          score: 1,
          box: { x: 0, y: 0, width: 0, height: 0 },
          thumbnailBase64: payload.faceImageBase64
        }
      ]);
    }

    if (!firstCamera) {
      notifications.show({
        title: t(
          'pages.persons.scan.notifications.noSystemCamera.title',
          undefined,
          'No system camera available'
        ),
        message: t(
          'pages.persons.scan.notifications.noSystemCamera.message',
          undefined,
          'QR data was applied to the form, but face enroll still requires at least one configured system camera.'
        ),
        color: 'yellow'
      });
    }

    personModal.open();
  };

  const onEnrollSubmit = async (values: EnrollFormValues) => {
    if (!selectedPerson) {
      notifications.show({
        title: t('pages.persons.notifications.selectPerson.title'),
        message: t('pages.persons.notifications.selectPerson.message'),
        color: 'red'
      });
      return;
    }

    if (!values.imageFile) {
      enrollForm.setFieldError('imageFile', t('pages.persons.validation.faceImageRequired'));
      return;
    }

    const base64 =
      selectedDetectedFace?.thumbnailBase64 ?? (await fileToBase64(values.imageFile));
    const payload: EnrollFaceRequest = {
      cameraId: values.cameraId,
      imageBase64: base64,
      storeFaceImage: true
    };

    enrollMutation.mutate({ personId: selectedPerson.id, payload });
  };

  const confirmTemplateDelete = (template: FaceTemplateResponse) => {
    if (!templatePerson) {
      return;
    }

    modals.openConfirmModal({
      title: t('pages.persons.confirmations.templateDeactivate.title'),
      children: (
        <Text size="sm">
          {t('pages.persons.confirmations.templateDeactivate.message', {
            id: template.id.slice(0, 8),
            name: `${templatePerson.firstName} ${templatePerson.lastName}`.trim()
          })}
        </Text>
      ),
      labels: {
        confirm: t('pages.persons.confirmations.templateDeactivate.confirm'),
        cancel: t('common.actions.cancel')
      },
      confirmProps: { color: 'red' },
      onConfirm: () =>
        deleteTemplateMutation.mutate({ personId: templatePerson.id, templateId: template.id })
    });
  };

  const renderFaceThumb = (src?: string | null, size = 44) => (
    <Box
      style={{
        width: size,
        height: size,
        borderRadius: 8,
        overflow: 'hidden',
        border: '1px solid rgba(255,255,255,0.08)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'rgba(255,255,255,0.04)'
      }}
    >
      {src ? (
        <img
          src={src}
          alt={t('pages.persons.enroll.enrolledFaceAlt')}
          style={{ width: '100%', height: '100%', objectFit: 'cover' }}
        />
      ) : (
        <IconPhoto size={18} />
      )}
    </Box>
  );

  return (
    <Stack gap="lg" className="page">
      <Group justify="space-between" align="center">
        <Stack gap={4}>
          <Text size="sm" className="muted-text">
            {t('pages.persons.subtitle')}
          </Text>
          <Text size="xl" fw={600}>
            {t('pages.persons.title')}
          </Text>
        </Stack>
        <Group gap="sm">
          <Button variant="light" leftSection={<IconQrcode size={18} />} onClick={scanModal.open}>
            {t('pages.persons.actions.scan', undefined, 'Scan VNeID')}
          </Button>
          <Button leftSection={<IconPlus size={18} />} onClick={openCreate}>
            {t('pages.persons.actions.add')}
          </Button>
        </Group>
      </Group>

      <Group>
        <TextInput
          placeholder={t('pages.persons.search.placeholder')}
          value={search}
          onChange={(event) => setSearch(event.currentTarget.value)}
          style={{ flex: 1 }}
        />
      </Group>

      <Paper p="lg" radius="lg" className="surface-card">
        <ScrollArea>
          <Table highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('pages.persons.table.face')}</Table.Th>
                <Table.Th>{t('pages.persons.table.code')}</Table.Th>
                <Table.Th>{t('pages.persons.table.name')}</Table.Th>
                <Table.Th>{t('pages.persons.table.gender')}</Table.Th>
                <Table.Th>{t('pages.persons.table.age')}</Table.Th>
                <Table.Th>{t('pages.persons.table.category')}</Table.Th>
                <Table.Th>{t('pages.persons.table.listType')}</Table.Th>
                <Table.Th>{t('pages.persons.table.enroll')}</Table.Th>
                <Table.Th>{t('pages.persons.table.status')}</Table.Th>
                <Table.Th>{t('pages.persons.table.updated')}</Table.Th>
                <Table.Th>{t('pages.persons.table.actions')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {paged.map((person) => (
                <Table.Tr key={person.id}>
                  <Table.Td>{renderFaceThumb(person.enrolledFaceImageBase64)}</Table.Td>
                  <Table.Td>{person.code}</Table.Td>
                  <Table.Td>
                    {person.firstName} {person.lastName}
                  </Table.Td>
                  <Table.Td>{person.gender || '-'}</Table.Td>
                  <Table.Td>{person.age ?? '-'}</Table.Td>
                  <Table.Td>{person.category || '-'}</Table.Td>
                  <Table.Td>
                    <Badge color={getListTypeMeta(person.listType).color} variant="light">
                      {getListTypeMeta(person.listType).label}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Badge color={person.isEnrolled ? 'brand' : 'gray'} variant="light">
                      {person.isEnrolled
                        ? t('pages.persons.status.enrolled')
                        : t('pages.persons.status.notEnrolled')}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Badge color={person.isActive ? 'brand' : 'gray'} variant="light">
                      {person.isActive ? t('common.status.active') : t('common.status.disabled')}
                    </Badge>
                  </Table.Td>
                  <Table.Td>{formatDateTime(person.updatedAt, t)}</Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      <Tooltip label={t('pages.persons.actions.enrollFace')}>
                        <ActionIcon
                          variant="light"
                          color="brand"
                          onClick={() => openEnroll(person)}
                        >
                          <IconPhoto size={16} />
                        </ActionIcon>
                      </Tooltip>
                      <Tooltip label={t('pages.persons.actions.templates')}>
                        <ActionIcon
                          variant="light"
                          color="blue"
                          onClick={() => openTemplates(person)}
                        >
                          <IconListDetails size={16} />
                        </ActionIcon>
                      </Tooltip>
                      <Tooltip label={t('common.actions.edit')}>
                        <ActionIcon
                          variant="light"
                          color="amber"
                          onClick={() => openEdit(person)}
                        >
                          <IconEdit size={16} />
                        </ActionIcon>
                      </Tooltip>
                      <Tooltip label={t('common.actions.delete')}>
                        <ActionIcon
                          variant="light"
                          color="red"
                          onClick={() => confirmDelete(person)}
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
              from: paginationFrom,
              to: paginationTo,
              total: filtered.length
            })}
          </Text>
          <Group gap="xs">
            <Select
              data={pageSizeOptions}
              value={pageSize}
              onChange={(value) => setPageSize(value ?? '20')}
              w={120}
              size="xs"
              allowDeselect={false}
            />
            <Pagination
              value={page}
              onChange={setPage}
              total={totalPages}
              size="sm"
            />
          </Group>
        </Group>
      </Paper>

      <Modal
        opened={personOpened}
        onClose={closePersonModal}
        title={
          editing
            ? t('pages.persons.modals.editPerson.title')
            : t('pages.persons.modals.addPerson.title')
        }
        size="lg"
      >
        <form onSubmit={personForm.onSubmit(onSubmit)}>
          <Stack gap="md">
            {editing && (
              <Group justify="space-between">
                <Text size="sm" className="muted-text">
                  {t('pages.persons.enrollmentStatus.label')}
                </Text>
                <Badge color={editing.isEnrolled ? 'brand' : 'gray'} variant="light">
                  {editing.isEnrolled
                    ? t('pages.persons.status.enrolled')
                    : t('pages.persons.status.notEnrolled')}
                </Badge>
              </Group>
            )}
            <Group grow>
              <TextInput
                label={t('pages.persons.form.personalId', undefined, 'Personal ID')}
                placeholder={t(
                  'pages.persons.form.placeholders.personalId',
                  undefined,
                  'CCCD / VNeID personal ID'
                )}
                value={personForm.values.personalId ?? ''}
                onChange={(event) => {
                  const nextValue = event.currentTarget.value;
                  personForm.setFieldValue('personalId', nextValue);
                  personForm.setFieldValue('code', nextValue);
                }}
                error={personForm.errors.personalId}
              />
              <TextInput
                label={t('pages.persons.form.category')}
                placeholder={t('pages.persons.form.placeholders.category')}
                {...personForm.getInputProps('category')}
              />
            </Group>
            <Select
              label={t('pages.persons.form.listType.label')}
              placeholder={t('pages.persons.form.listType.placeholder')}
              data={listTypeOptions}
              value={personForm.values.listType ?? ''}
              onChange={(value) => personForm.setFieldValue('listType', value ?? '')}
            />
            <TextInput
              label={t('pages.persons.form.fullName', undefined, 'Họ và tên')}
              placeholder={t(
                'pages.persons.form.placeholders.fullName',
                undefined,
                'Nguyễn Văn A'
              )}
              value={personFullName}
              onChange={(event) => {
                const nextName = splitPersonName(event.currentTarget.value);
                personForm.setFieldValue('firstName', nextName.firstName);
                personForm.setFieldValue('lastName', nextName.lastName);
              }}
              error={personForm.errors.firstName}
            />
            <Group grow>
              <TextInput
                label={t('pages.persons.form.documentNumber', undefined, 'Số định danh cũ')}
                placeholder={t(
                  'pages.persons.form.placeholders.documentNumber',
                  undefined,
                  'Số định danh / CMND cũ'
                )}
                {...personForm.getInputProps('documentNumber')}
              />
              <TextInput
                label={t('pages.persons.form.gender')}
                placeholder={t('pages.persons.form.placeholders.gender')}
                {...personForm.getInputProps('gender')}
              />
            </Group>
            <Group grow>
              <NumberInput
                label={t('pages.persons.form.age')}
                placeholder={t('pages.persons.form.placeholders.age')}
                min={0}
                max={120}
                {...personForm.getInputProps('age')}
              />
            </Group>
            <Group grow>
              <TextInput
                type="date"
                label={t('pages.persons.form.dateOfBirth', undefined, 'Date of birth')}
                value={personForm.values.dateOfBirth ?? ''}
                onChange={(event) =>
                  personForm.setFieldValue('dateOfBirth', event.currentTarget.value || null)
                }
              />
              <TextInput
                type="date"
                label={t('pages.persons.form.dateOfIssue', undefined, 'Date of issue')}
                value={personForm.values.dateOfIssue ?? ''}
                onChange={(event) =>
                  personForm.setFieldValue('dateOfIssue', event.currentTarget.value || null)
                }
              />
            </Group>
            <Textarea
              label={t('pages.persons.form.address', undefined, 'Address')}
              placeholder={t(
                'pages.persons.form.placeholders.address',
                undefined,
                'Address from CCCD / VNeID'
              )}
              autosize
              minRows={2}
              {...personForm.getInputProps('address')}
            />
            <Textarea
              label={t('pages.persons.form.remarks')}
              placeholder={t('pages.persons.form.placeholders.remarks')}
              autosize
              minRows={2}
              {...personForm.getInputProps('remarks')}
            />
            {!editing && (
              <Paper p="md" radius="lg" className="surface-card strong">
                <Stack gap="sm">
                  <Group justify="space-between" align="center">
                    <Stack gap={2}>
                      <Text size="sm" fw={600}>
                        {t('pages.persons.enroll.title')}
                      </Text>
                      <Text size="xs" className="muted-text">
                        {t('pages.persons.enroll.subtitle')}
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
                      {t('pages.persons.enroll.noCamera')}
                    </Text>
                  )}
                  {createEnrollEnabled && (
                    <Stack gap="sm">
                      <Select
                        label={t('pages.persons.enroll.camera')}
                        placeholder={t('pages.persons.enroll.cameraPlaceholder')}
                        data={cameraOptions}
                        value={createEnrollForm.values.cameraId}
                        onChange={(value) => {
                          const next = value ?? '';
                          createEnrollForm.setFieldValue('cameraId', next);
                        }}
                        error={createEnrollForm.errors.cameraId}
                        searchable
                        nothingFoundMessage={t('pages.persons.enroll.noCameras')}
                      />
                      <FileInput
                        label={t('pages.persons.enroll.faceImage')}
                        placeholder={t('pages.persons.enroll.faceImagePlaceholder')}
                        accept="image/*"
                        clearable
                        value={createEnrollForm.values.imageFile}
                        onChange={(file) => createEnrollForm.setFieldValue('imageFile', file)}
                        error={createEnrollForm.errors.imageFile}
                      />
                      <Group justify="space-between" align="center">
                        <Button
                          size="xs"
                          variant="light"
                          leftSection={<IconSearch size={14} />}
                          loading={createDetectingFaces}
                          onClick={() =>
                            detectFacesFromFile(
                              createEnrollForm.values.imageFile,
                              setCreateDetectedFaces,
                              setCreateSelectedDetectedFace,
                              setCreateDetectError,
                              setCreateDetectingFaces
                            )
                          }
                        >
                          {t('pages.persons.enroll.detect.action')}
                        </Button>
                        {createSelectedDetectedFace && (
                          <Badge color="brand" variant="light">
                            {t('pages.persons.enroll.detect.selected')}
                          </Badge>
                        )}
                      </Group>
                      {createDetectError && (
                        <Text size="sm" c="red">
                          {createDetectError}
                        </Text>
                      )}
                      {createDetectedFaces.length > 0 && (
                        <Stack gap="xs">
                          <Text size="xs" className="muted-text">
                            {t('pages.persons.enroll.detect.results')}
                          </Text>
                          <Group gap="sm" wrap="wrap">
                            {createDetectedFaces.map((face) => (
                              <Paper
                                key={face.faceId}
                                p={4}
                                radius="md"
                                className="surface-card strong"
                                style={{
                                  cursor: 'pointer',
                                  border:
                                    createSelectedDetectedFace?.faceId === face.faceId
                                      ? '2px solid var(--mantine-color-brand-6)'
                                      : '1px solid var(--mantine-color-gray-4)'
                                }}
                                onClick={() => selectCreateDetectedFace(face)}
                              >
                                <img
                                  src={face.thumbnailBase64}
                                  alt={t('pages.persons.enroll.detect.faceAlt')}
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
                </Stack>
              </Paper>
            )}
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
                onClick={closePersonModal}
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

      <Modal
        opened={enrollOpened}
        onClose={closeEnroll}
        title={
          selectedPerson
            ? t('pages.persons.enroll.modalTitleWithName', {
                name: `${selectedPerson.firstName} ${selectedPerson.lastName}`.trim()
              })
            : t('pages.persons.enroll.modalTitle')
        }
        size="lg"
      >
        <form onSubmit={enrollForm.onSubmit(onEnrollSubmit)}>
          <Stack gap="md">
            <Select
              label={t('pages.persons.enroll.camera')}
              placeholder={t('pages.persons.enroll.cameraPlaceholder')}
              data={cameraOptions}
              value={enrollForm.values.cameraId}
              onChange={(value) => {
                const next = value ?? '';
                enrollForm.setFieldValue('cameraId', next);
              }}
              error={enrollForm.errors.cameraId}
              searchable
              nothingFoundMessage={t('pages.persons.enroll.noCameras')}
            />
            <FileInput
              label={t('pages.persons.enroll.faceImage')}
              placeholder={t('pages.persons.enroll.faceImagePlaceholder')}
              accept="image/*"
              clearable
              value={enrollForm.values.imageFile}
              onChange={(file) => enrollForm.setFieldValue('imageFile', file)}
              error={enrollForm.errors.imageFile}
            />
            <Group justify="space-between" align="center">
              <Button
                size="xs"
                variant="light"
                leftSection={<IconSearch size={14} />}
                loading={detectingFaces}
                onClick={() =>
                  detectFacesFromFile(
                    enrollForm.values.imageFile,
                    setDetectedFaces,
                    setSelectedDetectedFace,
                    setDetectError,
                    setDetectingFaces
                  )
                }
              >
                {t('pages.persons.enroll.detect.action')}
              </Button>
              {selectedDetectedFace && (
                <Badge color="brand" variant="light">
                  {t('pages.persons.enroll.detect.selected')}
                </Badge>
              )}
            </Group>
            {detectError && (
              <Text size="sm" c="red">
                {detectError}
              </Text>
            )}
            {detectedFaces.length > 0 && (
              <Stack gap="xs">
                <Text size="xs" className="muted-text">
                  {t('pages.persons.enroll.detect.results')}
                </Text>
                <Group gap="sm" wrap="wrap">
                  {detectedFaces.map((face) => (
                    <Paper
                      key={face.faceId}
                      p={4}
                      radius="md"
                      className="surface-card strong"
                      style={{
                        cursor: 'pointer',
                        border:
                          selectedDetectedFace?.faceId === face.faceId
                            ? '2px solid var(--mantine-color-brand-6)'
                            : '1px solid var(--mantine-color-gray-4)'
                      }}
                      onClick={() => selectDetectedFace(face)}
                    >
                      <img
                        src={face.thumbnailBase64}
                        alt={t('pages.persons.enroll.detect.faceAlt')}
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
            <Group justify="flex-end">
              <Button variant="subtle" leftSection={<IconX size={16} />} onClick={closeEnroll}>
                {t('common.actions.cancel')}
              </Button>
              <Button
                type="submit"
                leftSection={<IconCheck size={16} />}
                loading={enrollMutation.isPending}
              >
                {t('pages.persons.enroll.action')}
              </Button>
            </Group>
          </Stack>
        </form>
      </Modal>

      <Modal
        opened={templateOpened}
        onClose={templateModal.close}
        title={
          templatePerson
            ? t('pages.persons.templates.titleWithName', {
                name: `${templatePerson.firstName} ${templatePerson.lastName}`.trim()
              })
            : t('pages.persons.templates.title')
        }
        size="xl"
      >
        <Stack gap="md">
          <Group justify="space-between" align="center">
            <Text size="sm" className="muted-text">
              {t('pages.persons.templates.total', {
                total: (templatesQuery.data ?? []).length
              })}
            </Text>
            {templatePerson && (
              <Badge color={templatePerson.isEnrolled ? 'brand' : 'gray'} variant="light">
                {templatePerson.isEnrolled
                  ? t('pages.persons.status.enrolled')
                  : t('pages.persons.status.notEnrolled')}
              </Badge>
            )}
          </Group>

          <ScrollArea h={520} type="auto" offsetScrollbars>
            <Stack gap="sm">
              {(templatesQuery.data ?? []).length === 0 && (
                <Paper p="lg" radius="lg" className="surface-card">
                  <Text size="sm" className="muted-text">
                    {t('pages.persons.templates.empty')}
                  </Text>
                </Paper>
              )}
              {(templatesQuery.data ?? []).map((template) => (
                <Paper key={template.id} p="md" radius="lg" className="surface-card">
                  <Group align="flex-start" wrap="nowrap">
                    {renderFaceThumb(template.faceImageBase64, 72)}
                    <Stack gap={6} style={{ flex: 1 }}>
                      <Group justify="space-between" align="center">
                        <Stack gap={2}>
                          <Text fw={600}>{template.id.slice(0, 8)}</Text>
                          <Text size="xs" className="muted-text">
                            {template.id}
                          </Text>
                        </Stack>
                        <Badge color={template.isActive ? 'brand' : 'gray'} variant="light">
                          {template.isActive
                            ? t('common.status.active')
                            : t('common.status.disabled')}
                        </Badge>
                      </Group>
                      <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm">
                        <Stack gap={2}>
                          <Text size="xs" className="muted-text">
                            {t('pages.persons.templates.fields.version')}
                          </Text>
                          <Text size="sm">{template.featureVersion || '-'}</Text>
                        </Stack>
                        <Stack gap={2}>
                          <Text size="xs" className="muted-text">
                            {t('pages.persons.templates.fields.l2Norm')}
                          </Text>
                          <Text size="sm">{template.l2Norm.toFixed(4)}</Text>
                        </Stack>
                        <Stack gap={2}>
                          <Text size="xs" className="muted-text">
                            {t('pages.persons.templates.fields.source')}
                          </Text>
                          <Text size="sm">{template.sourceCameraId ?? '-'}</Text>
                        </Stack>
                        <Stack gap={2}>
                          <Text size="xs" className="muted-text">
                            {t('pages.persons.templates.fields.updated')}
                          </Text>
                          <Text size="sm">{formatDateTime(template.updatedAt, t)}</Text>
                        </Stack>
                        <Stack gap={2}>
                          <Text size="xs" className="muted-text">
                            {t('pages.persons.templates.fields.created')}
                          </Text>
                          <Text size="sm">{formatDateTime(template.createdAt, t)}</Text>
                        </Stack>
                      </SimpleGrid>
                    </Stack>
                    <Group gap="sm" align="center">
                      <Switch
                        checked={template.isActive}
                        label={
                          template.isActive
                            ? t('common.status.active')
                            : t('common.status.disabled')
                        }
                        disabled={togglingTemplateId === template.id}
                        onChange={(event) => {
                          if (!templatePerson) {
                            return;
                          }
                          toggleTemplateMutation.mutate({
                            personId: templatePerson.id,
                            templateId: template.id,
                            isActive: event.currentTarget.checked
                          });
                        }}
                      />
                      <Tooltip label={t('pages.persons.actions.deactivateTemplate')}>
                        <ActionIcon
                          variant="light"
                          color="red"
                          onClick={() => confirmTemplateDelete(template)}
                        >
                          <IconTrash size={16} />
                        </ActionIcon>
                      </Tooltip>
                    </Group>
                  </Group>
                </Paper>
              ))}
            </Stack>
          </ScrollArea>
        </Stack>
      </Modal>

      <PersonScanModal
        opened={scanOpened}
        onClose={scanModal.close}
        onUseInForm={handleUseScannedInForm}
        cameraOptions={cameraOptions}
        t={t}
      />
    </Stack>
  );
}
