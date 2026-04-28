import { Badge } from '@mantine/core';

interface StatusPillProps {
  label: string;
  status: 'healthy' | 'degraded' | 'down' | 'checking';
}

export function StatusPill({ label, status }: StatusPillProps) {
  const config = {
    healthy: { color: 'brand', variant: 'light' },
    degraded: { color: 'amber', variant: 'light' },
    down: { color: 'red', variant: 'light' },
    checking: { color: 'gray', variant: 'light' }
  } as const;

  const style = config[status];
  return (
    <Badge color={style.color} variant={style.variant}>
      {label}
    </Badge>
  );
}
