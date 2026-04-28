import { Stack } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { listCameras } from '../api/ingestor';
import { MapLayoutManagerPanel } from '../components/MapLayoutManagerPanel';

export function Maps() {
  const camerasQuery = useQuery({
    queryKey: ['cameras'],
    queryFn: listCameras
  });

  return (
    <Stack gap="md" className="page" style={{ height: '100%', minHeight: 0 }}>
      <MapLayoutManagerPanel cameras={camerasQuery.data ?? []} />
    </Stack>
  );
}
