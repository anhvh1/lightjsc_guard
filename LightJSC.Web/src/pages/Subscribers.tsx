import {
  ActionIcon,
  Badge,
  Button,
  Group,
  Modal,
  Pagination,
  Paper,
  ScrollArea,
  Select,
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
import { IconCheck, IconEdit, IconPlus, IconTrash, IconX } from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from '@mantine/form';
import { useEffect, useMemo, useState } from 'react';
import { createSubscriber, deleteSubscriber, listSubscribers, updateSubscriber } from '../api/ingestor';
import type { SubscriberRequest, SubscriberResponse } from '../api/types';
import { useI18n } from '../i18n/I18nProvider';
import { formatDateTime } from '../utils/format';

const defaultValues: SubscriberRequest = {
  name: '',
  endpointUrl: '',
  enabled: true
};

export function Subscribers() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const [opened, { open, close }] = useDisclosure(false);
  const [editing, setEditing] = useState<SubscriberResponse | null>(null);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState('20');

  const subscribersQuery = useQuery({
    queryKey: ['subscribers'],
    queryFn: listSubscribers,
    refetchInterval: 30000
  });

  const pageSizeValue = Number(pageSize);
  const totalCount = subscribersQuery.data?.length ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSizeValue));
  const fromIndex = totalCount ? (page - 1) * pageSizeValue + 1 : 0;
  const toIndex = Math.min(page * pageSizeValue, totalCount);
  const pagedSubscribers = useMemo(() => {
    const list = subscribersQuery.data ?? [];
    const start = (page - 1) * pageSizeValue;
    return list.slice(start, start + pageSizeValue);
  }, [subscribersQuery.data, page, pageSizeValue]);

  useEffect(() => {
    setPage(1);
  }, [pageSizeValue]);

  useEffect(() => {
    if (page > totalPages) {
      setPage(totalPages);
    }
  }, [page, totalPages]);

  const form = useForm<SubscriberRequest>({
    initialValues: defaultValues,
    validate: {
      name: (value) =>
        value.trim().length === 0 ? t('pages.subscribers.validation.nameRequired') : null,
      endpointUrl: (value) =>
        value.trim().length === 0 ? t('pages.subscribers.validation.endpointRequired') : null
    }
  });

  const createMutation = useMutation({
    mutationFn: createSubscriber,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['subscribers'] });
      notifications.show({
        title: t('pages.subscribers.notifications.create.title'),
        message: t('pages.subscribers.notifications.create.message'),
        color: 'brand'
      });
      close();
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.subscribers.notifications.createFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: SubscriberRequest }) =>
      updateSubscriber(id, payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['subscribers'] });
      notifications.show({
        title: t('pages.subscribers.notifications.update.title'),
        message: t('pages.subscribers.notifications.update.message'),
        color: 'brand'
      });
      close();
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.subscribers.notifications.updateFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const deleteMutation = useMutation({
    mutationFn: deleteSubscriber,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['subscribers'] });
      notifications.show({
        title: t('pages.subscribers.notifications.delete.title'),
        message: t('pages.subscribers.notifications.delete.message'),
        color: 'brand'
      });
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.subscribers.notifications.deleteFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const confirmDelete = (subscriber: SubscriberResponse) => {
    modals.openConfirmModal({
      title: t('pages.subscribers.modals.delete.title'),
      children: (
        <Text size="sm">
          {t('pages.subscribers.modals.delete.message', {
            endpoint: subscriber.endpointUrl
          })}
        </Text>
      ),
      labels: { confirm: t('common.actions.delete'), cancel: t('common.actions.cancel') },
      confirmProps: { color: 'red' },
      onConfirm: () => deleteMutation.mutate(subscriber.id)
    });
  };

  const openCreate = () => {
    setEditing(null);
    form.setValues(defaultValues);
    open();
  };

  const openEdit = (subscriber: SubscriberResponse) => {
    setEditing(subscriber);
    form.setValues({
      name: subscriber.name,
      endpointUrl: subscriber.endpointUrl,
      enabled: subscriber.enabled
    });
    open();
  };

  const onSubmit = (values: SubscriberRequest) => {
    if (editing) {
      updateMutation.mutate({ id: editing.id, payload: values });
      return;
    }

    createMutation.mutate(values);
  };

  return (
    <Stack gap="lg" className="page">
      <Group justify="space-between" align="center">
        <Stack gap={4}>
          <Text size="sm" className="muted-text">
            {t('pages.subscribers.subtitle')}
          </Text>
          <Text size="xl" fw={600}>
            {t('pages.subscribers.title')}
          </Text>
        </Stack>
        <Button leftSection={<IconPlus size={18} />} onClick={openCreate}>
          {t('pages.subscribers.actions.add')}
        </Button>
      </Group>

      <Paper p="lg" radius="lg" className="surface-card">
        <ScrollArea>
          <Table highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('pages.subscribers.table.name')}</Table.Th>
                <Table.Th>{t('pages.subscribers.table.endpoint')}</Table.Th>
                <Table.Th>{t('pages.subscribers.table.status')}</Table.Th>
                <Table.Th>{t('pages.subscribers.table.created')}</Table.Th>
                <Table.Th>{t('pages.subscribers.table.actions')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {pagedSubscribers.map((subscriber) => (
                <Table.Tr key={subscriber.id}>
                  <Table.Td>{subscriber.name}</Table.Td>
                  <Table.Td>{subscriber.endpointUrl}</Table.Td>
                  <Table.Td>
                    <Badge color={subscriber.enabled ? 'brand' : 'gray'} variant="light">
                      {subscriber.enabled ? t('common.status.enabled') : t('common.status.disabled')}
                    </Badge>
                  </Table.Td>
                  <Table.Td>{formatDateTime(subscriber.createdAt, t)}</Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      <Tooltip label={t('common.actions.edit')}>
                        <ActionIcon variant="light" onClick={() => openEdit(subscriber)}>
                          <IconEdit size={16} />
                        </ActionIcon>
                      </Tooltip>
                      <Tooltip label={t('common.actions.delete')}>
                        <ActionIcon
                          variant="light"
                          color="red"
                          onClick={() => confirmDelete(subscriber)}
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
        opened={opened}
        onClose={close}
        title={
          editing
            ? t('pages.subscribers.modals.edit.title')
            : t('pages.subscribers.modals.create.title')
        }
        size="lg"
      >
        <form onSubmit={form.onSubmit(onSubmit)}>
          <Stack gap="md">
            <TextInput
              label={t('pages.subscribers.form.name')}
              placeholder={t('pages.subscribers.form.placeholders.name')}
              {...form.getInputProps('name')}
            />
            <TextInput
              label={t('pages.subscribers.form.endpointUrl')}
              placeholder={t('pages.subscribers.form.placeholders.endpointUrl')}
              {...form.getInputProps('endpointUrl')}
            />
            <Switch
              label={t('pages.subscribers.form.enabled')}
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
                loading={createMutation.isPending || updateMutation.isPending}
              >
                {editing ? t('common.actions.update') : t('common.actions.save')}
              </Button>
            </Group>
          </Stack>
        </form>
      </Modal>
    </Stack>
  );
}
