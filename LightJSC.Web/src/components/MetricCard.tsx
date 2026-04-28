import { Group, Paper, Stack, Text, ThemeIcon } from '@mantine/core';

interface MetricCardProps {
  label: string;
  value: string | number;
  icon: React.ReactNode;
  accent?: 'brand' | 'amber';
}

export function MetricCard({ label, value, icon, accent = 'brand' }: MetricCardProps) {
  return (
    <Paper p="lg" radius="lg" className="surface-card">
      <Group justify="space-between" align="flex-start">
        <Stack gap={4}>
          <Text size="sm" className="muted-text">
            {label}
          </Text>
          <Text size="xl" fw={600}>
            {value}
          </Text>
        </Stack>
        <ThemeIcon size="lg" radius="md" color={accent} variant="light">
          {icon}
        </ThemeIcon>
      </Group>
    </Paper>
  );
}
