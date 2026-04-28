import {
  Badge,
  Button,
  Group,
  NumberInput,
  Paper,
  SimpleGrid,
  Stack,
  Switch,
  Text,
  TextInput,
  Textarea
} from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { IconCheck, IconRefresh } from '@tabler/icons-react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import {
  getAlarmDeliverySettings,
  getBestshotSettings,
  getMatchingSettings,
  reembedEvents,
  updateAlarmDeliverySettings,
  updateBestshotSettings,
  updateMatchingSettings
} from '../api/ingestor';
import type {
  AlarmDeliverySettings,
  BestshotSettings,
  MatchingSettings,
  ReembedEventsRequest,
  ReembedResult
} from '../api/types';
import { useI18n } from '../i18n/I18nProvider';

const defaultSettings: AlarmDeliverySettings = {
  sendWhiteList: true,
  sendBlackList: true,
  sendProtect: true,
  sendUndefined: true
};

const defaultMatchingSettings: MatchingSettings = {
  similarity: 0.4,
  score: 0.2
};

const defaultBestshotSettings: BestshotSettings = {
  rootPath: 'bestshots',
  retentionDays: 90
};

type ReembedFormValues = {
  fromUtc: string;
  toUtc: string;
  maxEvents: number;
  dryRun: boolean;
  targetFeatureVersion: string;
  cameraIds: string;
  featureVersionByCamera: string;
};

const defaultReembedValues: ReembedFormValues = {
  fromUtc: '',
  toUtc: '',
  maxEvents: 5000,
  dryRun: true,
  targetFeatureVersion: '',
  cameraIds: '',
  featureVersionByCamera: ''
};

export function Settings() {
  const { t } = useI18n();
  const form = useForm<AlarmDeliverySettings>({
    initialValues: defaultSettings
  });

  const matchingForm = useForm<MatchingSettings>({
    initialValues: defaultMatchingSettings,
    validate: {
      similarity: (value) =>
        value < 0 || value > 1 ? t('pages.settings.validation.similarityRange') : null,
      score: (value) =>
        value < 0 || value > 1 ? t('pages.settings.validation.scoreRange') : null
    }
  });

  const bestshotForm = useForm<BestshotSettings>({
    initialValues: defaultBestshotSettings,
    validate: {
      retentionDays: (value) =>
        value < 0 ? t('pages.settings.validation.retentionDays') : null
    }
  });

  const reembedForm = useForm<ReembedFormValues>({
    initialValues: defaultReembedValues,
    validate: {
      fromUtc: (value) =>
        value && Number.isNaN(Date.parse(value)) ? t('pages.settings.validation.invalidUtc') : null,
      toUtc: (value) =>
        value && Number.isNaN(Date.parse(value)) ? t('pages.settings.validation.invalidUtc') : null,
      maxEvents: (value) => (value < 1 ? t('pages.settings.validation.maxEvents') : null)
    }
  });

  const [reembedResult, setReembedResult] = useState<ReembedResult | null>(null);

  const settingsQuery = useQuery({
    queryKey: ['settings', 'alarm-delivery'],
    queryFn: getAlarmDeliverySettings
  });

  const matchingQuery = useQuery({
    queryKey: ['settings', 'matching'],
    queryFn: getMatchingSettings
  });

  const bestshotQuery = useQuery({
    queryKey: ['settings', 'bestshot'],
    queryFn: getBestshotSettings
  });

  const saveAlarmMutation = useMutation({
    mutationFn: updateAlarmDeliverySettings,
    onSuccess: (result) => {
      form.setValues(result);
      form.resetDirty(result);
      notifications.show({
        title: t('pages.settings.notifications.alarmSaved.title'),
        message: t('pages.settings.notifications.alarmSaved.message'),
        color: 'brand'
      });
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.settings.notifications.saveFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const saveMatchingMutation = useMutation({
    mutationFn: updateMatchingSettings,
    onSuccess: (result) => {
      matchingForm.setValues(result);
      matchingForm.resetDirty(result);
      notifications.show({
        title: t('pages.settings.notifications.matchingSaved.title'),
        message: t('pages.settings.notifications.matchingSaved.message'),
        color: 'brand'
      });
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.settings.notifications.saveFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const saveBestshotMutation = useMutation({
    mutationFn: updateBestshotSettings,
    onSuccess: (result) => {
      bestshotForm.setValues(result);
      bestshotForm.resetDirty(result);
      notifications.show({
        title: t('pages.settings.notifications.bestshotSaved.title'),
        message: t('pages.settings.notifications.bestshotSaved.message'),
        color: 'brand'
      });
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.settings.notifications.saveFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const reembedMutation = useMutation({
    mutationFn: reembedEvents,
    onSuccess: (result) => {
      setReembedResult(result);
      notifications.show({
        title: t('pages.settings.notifications.reembedFinished.title'),
        message: t('pages.settings.notifications.reembedFinished.message', {
          processed: result.processed,
          created: result.created
        }),
        color: result.failed > 0 ? 'orange' : 'brand'
      });
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.settings.notifications.reembedFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  useEffect(() => {
    if (settingsQuery.data) {
      form.setValues(settingsQuery.data);
      form.resetDirty(settingsQuery.data);
    }
  }, [settingsQuery.data]);

  useEffect(() => {
    if (matchingQuery.data) {
      matchingForm.setValues(matchingQuery.data);
      matchingForm.resetDirty(matchingQuery.data);
    }
  }, [matchingQuery.data]);

  useEffect(() => {
    if (bestshotQuery.data) {
      bestshotForm.setValues(bestshotQuery.data);
      bestshotForm.resetDirty(bestshotQuery.data);
    }
  }, [bestshotQuery.data]);

  const activeCount = useMemo(() => {
    return Object.values(form.values).filter(Boolean).length;
  }, [form.values]);

  const onSaveAlarm = () => {
    saveAlarmMutation.mutate(form.values);
  };

  const onSaveMatching = () => {
    const validation = matchingForm.validate();
    if (validation.hasErrors) {
      return;
    }

    saveMatchingMutation.mutate(matchingForm.values);
  };

  const onSaveBestshot = () => {
    const validation = bestshotForm.validate();
    if (validation.hasErrors) {
      return;
    }

    saveBestshotMutation.mutate(bestshotForm.values);
  };

  const parseDateInput = (value: string) => {
    const trimmed = value.trim();
    if (!trimmed) {
      return undefined;
    }

    const parsed = Date.parse(trimmed);
    if (Number.isNaN(parsed)) {
      return null;
    }

    return new Date(parsed).toISOString();
  };

  const parseCameraIds = (value: string) => {
    return value
      .split(/[\s,]+/)
      .map((item) => item.trim())
      .filter((item) => item.length > 0);
  };

  const parseFeatureVersionMap = (value: string) => {
    const trimmed = value.trim();
    if (!trimmed) {
      return undefined;
    }

    try {
      const parsed = JSON.parse(trimmed);
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
        return null;
      }

      const normalized: Record<string, string> = {};
      Object.entries(parsed).forEach(([key, rawValue]) => {
        if (typeof rawValue !== 'string') {
          return;
        }

        const normalizedKey = key.trim();
        const normalizedValue = rawValue.trim();
        if (normalizedKey && normalizedValue) {
          normalized[normalizedKey] = normalizedValue;
        }
      });

      return Object.keys(normalized).length > 0 ? normalized : undefined;
    } catch (error) {
      return null;
    }
  };

  const onRunReembed = () => {
    const validation = reembedForm.validate();
    if (validation.hasErrors) {
      return;
    }

    const request: ReembedEventsRequest = {
      maxEvents: Math.max(1, Math.floor(reembedForm.values.maxEvents || 0)),
      dryRun: reembedForm.values.dryRun
    };

    const fromUtc = parseDateInput(reembedForm.values.fromUtc);
    if (fromUtc === null) {
      reembedForm.setFieldError('fromUtc', t('pages.settings.validation.invalidUtc'));
      return;
    }
    if (fromUtc) {
      request.fromUtc = fromUtc;
    }

    const toUtc = parseDateInput(reembedForm.values.toUtc);
    if (toUtc === null) {
      reembedForm.setFieldError('toUtc', t('pages.settings.validation.invalidUtc'));
      return;
    }
    if (toUtc) {
      request.toUtc = toUtc;
    }

    const cameraIds = parseCameraIds(reembedForm.values.cameraIds);
    if (cameraIds.length > 0) {
      request.cameraIds = cameraIds;
    }

    const featureVersionByCamera = parseFeatureVersionMap(
      reembedForm.values.featureVersionByCamera
    );
    if (featureVersionByCamera === null) {
      reembedForm.setFieldError(
        'featureVersionByCamera',
        t('pages.settings.validation.invalidJsonMap')
      );
      return;
    }
    if (featureVersionByCamera) {
      request.featureVersionByCamera = featureVersionByCamera;
    }

    const targetFeatureVersion = reembedForm.values.targetFeatureVersion.trim();
    if (targetFeatureVersion) {
      request.targetFeatureVersion = targetFeatureVersion;
    }

    setReembedResult(null);
    reembedMutation.mutate(request);
  };

  const alarmBusy = settingsQuery.isLoading || saveAlarmMutation.isPending;
  const matchingBusy = matchingQuery.isLoading || saveMatchingMutation.isPending;
  const bestshotBusy = bestshotQuery.isLoading || saveBestshotMutation.isPending;
  const reembedBusy = reembedMutation.isPending;

  return (
    <Stack gap="lg" className="page">
      <Group justify="space-between" align="center">
        <Stack gap={4}>
          <Text size="sm" className="muted-text">
            {t('pages.settings.subtitle')}
          </Text>
          <Text size="xl" fw={600}>
            {t('pages.settings.title')}
          </Text>
        </Stack>
        <Badge color={activeCount > 0 ? 'brand' : 'gray'} variant="light">
          {t('pages.settings.badge.enabled', { count: activeCount })}
        </Badge>
      </Group>

      <Paper p="lg" radius="lg" className="surface-card">
        <Stack gap="md">
          <Group justify="space-between" align="center">
            <Stack gap={4}>
              <Text size="sm" fw={600}>
                {t('pages.settings.alarm.title')}
              </Text>
              <Text size="xs" className="muted-text">
                {t('pages.settings.alarm.subtitle')}
              </Text>
            </Stack>
            <Badge color="blue" variant="light">
              {t('pages.settings.alarm.badge')}
            </Badge>
          </Group>
          <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
            <Paper p="md" radius="lg" className="surface-card strong">
              <Switch
                label={t('pages.settings.alarm.filters.white.label')}
                description={t('pages.settings.alarm.filters.white.description')}
                checked={form.values.sendWhiteList}
                disabled={alarmBusy}
                onChange={(event) =>
                  form.setFieldValue('sendWhiteList', event.currentTarget.checked)
                }
                size="md"
              />
            </Paper>
            <Paper p="md" radius="lg" className="surface-card strong">
              <Switch
                label={t('pages.settings.alarm.filters.black.label')}
                description={t('pages.settings.alarm.filters.black.description')}
                checked={form.values.sendBlackList}
                disabled={alarmBusy}
                onChange={(event) =>
                  form.setFieldValue('sendBlackList', event.currentTarget.checked)
                }
                size="md"
              />
            </Paper>
            <Paper p="md" radius="lg" className="surface-card strong">
              <Switch
                label={t('pages.settings.alarm.filters.protect.label')}
                description={t('pages.settings.alarm.filters.protect.description')}
                checked={form.values.sendProtect}
                disabled={alarmBusy}
                onChange={(event) =>
                  form.setFieldValue('sendProtect', event.currentTarget.checked)
                }
                size="md"
              />
            </Paper>
            <Paper p="md" radius="lg" className="surface-card strong">
              <Switch
                label={t('pages.settings.alarm.filters.undefined.label')}
                description={t('pages.settings.alarm.filters.undefined.description')}
                checked={form.values.sendUndefined}
                disabled={alarmBusy}
                onChange={(event) =>
                  form.setFieldValue('sendUndefined', event.currentTarget.checked)
                }
                size="md"
              />
            </Paper>
          </SimpleGrid>
          <Group justify="flex-end">
            <Button
              variant="light"
              leftSection={<IconCheck size={18} />}
              onClick={onSaveAlarm}
              loading={saveAlarmMutation.isPending}
              disabled={alarmBusy}
            >
              {t('common.actions.apply')}
            </Button>
          </Group>
        </Stack>
      </Paper>

      <Paper p="lg" radius="lg" className="surface-card">
        <Stack gap="md">
          <Group justify="space-between" align="center">
            <Stack gap={4}>
              <Text size="sm" fw={600}>
                {t('pages.settings.bestshot.title')}
              </Text>
              <Text size="xs" className="muted-text">
                {t('pages.settings.bestshot.subtitle')}
              </Text>
            </Stack>
            <Badge color="teal" variant="light">
              {t('pages.settings.bestshot.badge')}
            </Badge>
          </Group>
          <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
            <Paper p="md" radius="lg" className="surface-card strong">
              <Stack gap={6}>
                <Text size="sm" fw={600}>
                  {t('pages.settings.bestshot.folder.title')}
                </Text>
                <Text size="xs" className="muted-text">
                  {t('pages.settings.bestshot.folder.subtitle')}
                </Text>
                <TextInput
                  value={bestshotForm.values.rootPath}
                  onChange={(event) =>
                    bestshotForm.setFieldValue('rootPath', event.currentTarget.value)
                  }
                  placeholder={t('pages.settings.bestshot.folder.placeholder')}
                  disabled={bestshotBusy}
                />
              </Stack>
            </Paper>
            <Paper p="md" radius="lg" className="surface-card strong">
              <Stack gap={6}>
                <Text size="sm" fw={600}>
                  {t('pages.settings.bestshot.retention.title')}
                </Text>
                <Text size="xs" className="muted-text">
                  {t('pages.settings.bestshot.retention.subtitle')}
                </Text>
                <NumberInput
                  value={bestshotForm.values.retentionDays}
                  onChange={(value) =>
                    bestshotForm.setFieldValue(
                      'retentionDays',
                      typeof value === 'number' ? value : 0
                    )
                  }
                  min={0}
                  step={1}
                  disabled={bestshotBusy}
                  error={bestshotForm.errors.retentionDays}
                />
              </Stack>
            </Paper>
          </SimpleGrid>
          <Group justify="flex-end">
            <Button
              variant="light"
              leftSection={<IconCheck size={18} />}
              onClick={onSaveBestshot}
              loading={saveBestshotMutation.isPending}
              disabled={bestshotBusy}
            >
              {t('common.actions.apply')}
            </Button>
          </Group>
        </Stack>
      </Paper>

      <Paper p="lg" radius="lg" className="surface-card">
        <Stack gap="md">
          <Group justify="space-between" align="center">
            <Stack gap={4}>
              <Text size="sm" fw={600}>
                {t('pages.settings.matching.title')}
              </Text>
              <Text size="xs" className="muted-text">
                {t('pages.settings.matching.subtitle')}
              </Text>
            </Stack>
            <Badge color="grape" variant="light">
              {t('pages.settings.matching.badge')}
            </Badge>
          </Group>
          <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
            <Paper p="md" radius="lg" className="surface-card strong">
              <Stack gap={6}>
                <Text size="sm" fw={600}>
                  {t('pages.settings.matching.similarity.title')}
                </Text>
                <Text size="xs" className="muted-text">
                  {t('pages.settings.matching.similarity.subtitle')}
                </Text>
                <NumberInput
                  value={matchingForm.values.similarity}
                  onChange={(value) =>
                    matchingForm.setFieldValue(
                      'similarity',
                      typeof value === 'number' ? value : 0
                    )
                  }
                  min={0}
                  max={1}
                  step={0.01}
                  decimalScale={2}
                  disabled={matchingBusy}
                  error={matchingForm.errors.similarity}
                />
              </Stack>
            </Paper>
            <Paper p="md" radius="lg" className="surface-card strong">
              <Stack gap={6}>
                <Text size="sm" fw={600}>
                  {t('pages.settings.matching.score.title')}
                </Text>
                <Text size="xs" className="muted-text">
                  {t('pages.settings.matching.score.subtitle')}
                </Text>
                <NumberInput
                  value={matchingForm.values.score}
                  onChange={(value) =>
                    matchingForm.setFieldValue('score', typeof value === 'number' ? value : 0)
                  }
                  min={0}
                  max={1}
                  step={0.01}
                  decimalScale={2}
                  disabled={matchingBusy}
                  error={matchingForm.errors.score}
                />
              </Stack>
            </Paper>
          </SimpleGrid>
          <Group justify="flex-end">
            <Button
              variant="light"
              leftSection={<IconCheck size={18} />}
              onClick={onSaveMatching}
              loading={saveMatchingMutation.isPending}
              disabled={matchingBusy}
            >
              {t('common.actions.apply')}
            </Button>
          </Group>
        </Stack>
      </Paper>

      <Paper p="lg" radius="lg" className="surface-card">
        <Stack gap="md">
          <Group justify="space-between" align="center">
            <Stack gap={4}>
              <Text size="sm" fw={600}>
                {t('pages.settings.reembed.title')}
              </Text>
              <Text size="xs" className="muted-text">
                {t('pages.settings.reembed.subtitle')}
              </Text>
            </Stack>
            <Badge color="orange" variant="light">
              {t('pages.settings.reembed.badge')}
            </Badge>
          </Group>
          <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
            <Paper p="md" radius="lg" className="surface-card strong">
              <Stack gap={6}>
                <Text size="sm" fw={600}>
                  {t('pages.settings.reembed.fromUtc.title')}
                </Text>
                <Text size="xs" className="muted-text">
                  {t('pages.settings.reembed.fromUtc.subtitle')}
                </Text>
                <TextInput
                  value={reembedForm.values.fromUtc}
                  onChange={(event) =>
                    reembedForm.setFieldValue('fromUtc', event.currentTarget.value)
                  }
                  placeholder={t('pages.settings.reembed.fromUtc.placeholder')}
                  disabled={reembedBusy}
                  error={reembedForm.errors.fromUtc}
                />
              </Stack>
            </Paper>
            <Paper p="md" radius="lg" className="surface-card strong">
              <Stack gap={6}>
                <Text size="sm" fw={600}>
                  {t('pages.settings.reembed.toUtc.title')}
                </Text>
                <Text size="xs" className="muted-text">
                  {t('pages.settings.reembed.toUtc.subtitle')}
                </Text>
                <TextInput
                  value={reembedForm.values.toUtc}
                  onChange={(event) =>
                    reembedForm.setFieldValue('toUtc', event.currentTarget.value)
                  }
                  placeholder={t('pages.settings.reembed.toUtc.placeholder')}
                  disabled={reembedBusy}
                  error={reembedForm.errors.toUtc}
                />
              </Stack>
            </Paper>
            <Paper p="md" radius="lg" className="surface-card strong">
              <Stack gap={6}>
                <Text size="sm" fw={600}>
                  {t('pages.settings.reembed.cameraIds.title')}
                </Text>
                <Text size="xs" className="muted-text">
                  {t('pages.settings.reembed.cameraIds.subtitle')}
                </Text>
                <TextInput
                  value={reembedForm.values.cameraIds}
                  onChange={(event) =>
                    reembedForm.setFieldValue('cameraIds', event.currentTarget.value)
                  }
                  placeholder={t('pages.settings.reembed.cameraIds.placeholder')}
                  disabled={reembedBusy}
                />
              </Stack>
            </Paper>
            <Paper p="md" radius="lg" className="surface-card strong">
              <Stack gap={6}>
                <Text size="sm" fw={600}>
                  {t('pages.settings.reembed.targetVersion.title')}
                </Text>
                <Text size="xs" className="muted-text">
                  {t('pages.settings.reembed.targetVersion.subtitle')}
                </Text>
                <TextInput
                  value={reembedForm.values.targetFeatureVersion}
                  onChange={(event) =>
                    reembedForm.setFieldValue('targetFeatureVersion', event.currentTarget.value)
                  }
                  placeholder={t('pages.settings.reembed.targetVersion.placeholder')}
                  disabled={reembedBusy}
                />
              </Stack>
            </Paper>
          </SimpleGrid>
          <Paper p="md" radius="lg" className="surface-card strong">
            <Stack gap={6}>
              <Text size="sm" fw={600}>
                {t('pages.settings.reembed.featureMap.title')}
              </Text>
              <Text size="xs" className="muted-text">
                {t('pages.settings.reembed.featureMap.subtitle', {
                  example: '{"cam-1":"0.1","cam-2":"0.2"}'
                })}
              </Text>
              <Textarea
                value={reembedForm.values.featureVersionByCamera}
                onChange={(event) =>
                  reembedForm.setFieldValue('featureVersionByCamera', event.currentTarget.value)
                }
                placeholder={t('pages.settings.reembed.featureMap.placeholder')}
                minRows={2}
                maxRows={4}
                autosize
                disabled={reembedBusy}
                error={reembedForm.errors.featureVersionByCamera}
              />
            </Stack>
          </Paper>
          <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
            <Paper p="md" radius="lg" className="surface-card strong">
              <Stack gap={6}>
                <Text size="sm" fw={600}>
                  {t('pages.settings.reembed.maxEvents.title')}
                </Text>
                <Text size="xs" className="muted-text">
                  {t('pages.settings.reembed.maxEvents.subtitle')}
                </Text>
                <NumberInput
                  value={reembedForm.values.maxEvents}
                  onChange={(value) =>
                    reembedForm.setFieldValue(
                      'maxEvents',
                      typeof value === 'number' ? value : 0
                    )
                  }
                  min={1}
                  max={5000}
                  step={500}
                  disabled={reembedBusy}
                  error={reembedForm.errors.maxEvents}
                />
              </Stack>
            </Paper>
            <Paper p="md" radius="lg" className="surface-card strong">
              <Stack gap={6}>
                <Text size="sm" fw={600}>
                  {t('pages.settings.reembed.dryRun.title')}
                </Text>
                <Text size="xs" className="muted-text">
                  {t('pages.settings.reembed.dryRun.subtitle')}
                </Text>
                <Switch
                  checked={reembedForm.values.dryRun}
                  onChange={(event) =>
                    reembedForm.setFieldValue('dryRun', event.currentTarget.checked)
                  }
                  size="md"
                  disabled={reembedBusy}
                  label={t('pages.settings.reembed.dryRun.label')}
                />
              </Stack>
            </Paper>
          </SimpleGrid>
          {reembedResult && (
            <Stack gap="xs">
              <Group gap="sm">
                <Badge color="blue" variant="light">
                  {t('pages.settings.reembed.result.processed', {
                    count: reembedResult.processed
                  })}
                </Badge>
                <Badge color="green" variant="light">
                  {t('pages.settings.reembed.result.created', {
                    count: reembedResult.created
                  })}
                </Badge>
                <Badge color="gray" variant="light">
                  {t('pages.settings.reembed.result.skipped', {
                    count: reembedResult.skipped
                  })}
                </Badge>
                <Badge color={reembedResult.failed > 0 ? 'red' : 'gray'} variant="light">
                  {t('pages.settings.reembed.result.failed', { count: reembedResult.failed })}
                </Badge>
              </Group>
              {reembedResult.errors && reembedResult.errors.length > 0 && (
                <Textarea
                  value={reembedResult.errors.join('\n')}
                  readOnly
                  minRows={2}
                  maxRows={6}
                  autosize
                />
              )}
            </Stack>
          )}
          <Group justify="flex-end">
            <Button
              variant="light"
              leftSection={<IconRefresh size={18} />}
              onClick={onRunReembed}
              loading={reembedMutation.isPending}
              disabled={reembedBusy}
            >
              {t('pages.settings.reembed.actions.run')}
            </Button>
          </Group>
        </Stack>
      </Paper>
    </Stack>
  );
}
