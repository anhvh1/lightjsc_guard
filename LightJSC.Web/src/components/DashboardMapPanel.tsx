import { ActionIcon, Box, Paper, Select, Stack, Text } from '@mantine/core';
import { useElementSize } from '@mantine/hooks';
import { IconMapPin, IconRefresh } from '@tabler/icons-react';
import * as signalR from '@microsoft/signalr';
import { useQuery } from '@tanstack/react-query';
import maplibregl, { type Map as MapLibreMap } from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { buildApiUrl } from '../api/client';
import { getDashboardMapHeat, getMap, getMapOptions, listCameras, listMaps } from '../api/ingestor';
import { buildSubscriberUrl } from '../api/subscriber';
import type { FaceEvent, MapLayoutResponse } from '../api/types';
import { useI18n } from '../i18n/I18nProvider';

const DEFAULT_CENTER: [number, number] = [106.7, 10.8];
const DEFAULT_ZOOM = 11;
const ALERT_WINDOW_MS = 10_000;
const ALERT_POLL_INTERVAL_MS = 2000;
const ALERT_SWEEP_INTERVAL_MS = 1000;
const BASE_ICON_SIZE = 28;
const ICON_ROTATION_OFFSET = -90;
const MIN_IMAGE_ZOOM = 0.2;
const MAX_IMAGE_ZOOM = 6;

type DashboardMapPanelProps = {
  autoRefresh: boolean;
};

type DragState = {
  startX: number;
  startY: number;
  originX: number;
  originY: number;
};

const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));

const normalizeAngle = (value: number) => {
  const normalized = value % 360;
  return normalized < 0 ? normalized + 360 : normalized;
};

const setsEqual = (left: Set<string>, right: Set<string>) => {
  if (left.size !== right.size) {
    return false;
  }
  for (const value of left) {
    if (!right.has(value)) {
      return false;
    }
  }
  return true;
};

const createCameraMarkerElement = () => {
  const wrapper = document.createElement('div');
  wrapper.className = 'dashboard-map-marker';

  const ring = document.createElement('div');
  ring.dataset.role = 'alert-ring';
  ring.className = 'dashboard-map-ring';

  const icon = document.createElement('div');
  icon.dataset.role = 'icon';
  icon.className = 'dashboard-map-icon';

  const label = document.createElement('div');
  label.dataset.role = 'label';
  label.className = 'dashboard-map-label';

  wrapper.appendChild(ring);
  wrapper.appendChild(icon);
  wrapper.appendChild(label);

  return wrapper;
};

const updateCameraMarkerElement = (
  element: HTMLElement,
  params: {
    label: string;
    angle: number;
    scale: number;
    active: boolean;
  }
) => {
  const ring = element.querySelector('[data-role="alert-ring"]') as HTMLDivElement | null;
  const icon = element.querySelector('[data-role="icon"]') as HTMLDivElement | null;
  const label = element.querySelector('[data-role="label"]') as HTMLDivElement | null;
  const iconSize = Math.max(20, Math.round(BASE_ICON_SIZE * params.scale));
  const ringSize = Math.round(iconSize * 1.9);

  if (icon) {
    icon.style.width = `${iconSize}px`;
    icon.style.height = `${iconSize}px`;
    icon.style.transform = `translate(-50%, -50%) rotate(${normalizeAngle(
      params.angle + ICON_ROTATION_OFFSET
    )}deg)`;
  }

  if (ring) {
    ring.style.width = `${ringSize}px`;
    ring.style.height = `${ringSize}px`;
    ring.classList.toggle('is-active', params.active);
  }

  if (label) {
    label.textContent = params.label;
    label.style.display = params.label ? 'block' : 'none';
  }

  element.title = params.label;
};

export function DashboardMapPanel({ autoRefresh }: DashboardMapPanelProps) {
  const { t } = useI18n();
  const [selectedMapId, setSelectedMapId] = useState<string | null>(null);
  const mapContainerRef = useRef<HTMLDivElement | null>(null);
  const mapInstanceRef = useRef<MapLibreMap | null>(null);
  const lastMapIdRef = useRef<string | null>(null);
  const mapMarkersRef = useRef<Map<string, maplibregl.Marker>>(new Map());
  const lastEventByCameraRef = useRef<Map<string, number>>(new Map());
  const [activeCameraIds, setActiveCameraIds] = useState<Set<string>>(new Set());
  const [imageNaturalSize, setImageNaturalSize] = useState({ width: 0, height: 0 });
  const [imageView, setImageView] = useState({ scale: 1, translateX: 0, translateY: 0 });
  const [isPanning, setIsPanning] = useState(false);
  const imageViewportRef = useRef<HTMLDivElement | null>(null);
  const dragStateRef = useRef<DragState | null>(null);
  const lastImageMapIdRef = useRef<string | null>(null);
  const imageViewInitializedRef = useRef(false);
  const { ref: imageViewportSizeRef, width: viewportWidth, height: viewportHeight } =
    useElementSize();
  const hubUrl = useMemo(() => buildSubscriberUrl('/hubs/faces'), []);

  const mapsQuery = useQuery({ queryKey: ['maps'], queryFn: listMaps });
  const mapOptionsQuery = useQuery({ queryKey: ['maps', 'options'], queryFn: getMapOptions });
  const camerasQuery = useQuery({
    queryKey: ['cameras'],
    queryFn: listCameras
  });

  useEffect(() => {
    if (!selectedMapId && mapsQuery.data?.length) {
      const firstGeo = mapsQuery.data.find((map: MapLayoutResponse) => map.type === 'Geo');
      setSelectedMapId(firstGeo?.id ?? mapsQuery.data[0].id);
    }
  }, [mapsQuery.data, selectedMapId]);

  const mapDetailQuery = useQuery({
    queryKey: ['maps', selectedMapId],
    queryFn: () => getMap(selectedMapId ?? ''),
    enabled: Boolean(selectedMapId)
  });

  const activityQuery = useQuery({
    queryKey: ['dashboard', 'map-activity', selectedMapId],
    queryFn: () => {
      const now = new Date();
      const from = new Date(now.getTime() - ALERT_WINDOW_MS);
      return getDashboardMapHeat({
        mapId: selectedMapId ?? undefined,
        fromUtc: from.toISOString(),
        toUtc: now.toISOString()
      });
    },
    enabled: Boolean(selectedMapId),
    refetchInterval: autoRefresh ? ALERT_POLL_INTERVAL_MS : false
  });

  const geoStyleUrl = mapOptionsQuery.data?.geoStyleUrl?.trim() ?? '';
  const activeMap = mapDetailQuery.data?.map;
  const mapPositions = mapDetailQuery.data?.cameras ?? [];

  const mapSelectOptions = useMemo(
    () =>
      (mapsQuery.data ?? []).map((map) => ({
        value: map.id,
        label: `${map.name} (${map.type})`
      })),
    [mapsQuery.data]
  );

  const cameraLabelById = useMemo(() => {
    return new Map(
      (camerasQuery.data ?? []).map((camera) => {
        const code = camera.code?.trim();
        const ip = camera.ipAddress?.trim();
        if (code && ip) {
          return [camera.cameraId, `${code} - ${ip}`];
        }
        return [camera.cameraId, code || ip || camera.cameraId];
      })
    );
  }, [camerasQuery.data]);

  const refreshActiveSet = useCallback(() => {
    const now = Date.now();
    const lastEvents = lastEventByCameraRef.current;
    const next = new Set<string>();
    for (const [cameraId, lastEventAt] of lastEvents.entries()) {
      if (now - lastEventAt <= ALERT_WINDOW_MS) {
        next.add(cameraId);
      } else {
        lastEvents.delete(cameraId);
      }
    }
    setActiveCameraIds((prev) => (setsEqual(prev, next) ? prev : next));
  }, []);

  useEffect(() => {
    lastEventByCameraRef.current.clear();
    setActiveCameraIds(new Set());
    if (activeMap?.type !== 'Image') {
      setImageNaturalSize({ width: 0, height: 0 });
      imageViewInitializedRef.current = false;
    }
  }, [selectedMapId]);

  useEffect(() => {
    let active = true;
    let retryTimer: number | undefined;
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect([1000, 3000, 5000, 10000])
      .build();

    connection.on('faceEvent', (event: FaceEvent) => {
      if (!active) {
        return;
      }
      if (!event.cameraId) {
        return;
      }
      lastEventByCameraRef.current.set(event.cameraId, Date.now());
      refreshActiveSet();
    });

    const startConnection = async () => {
      try {
        await connection.start();
      } catch (error) {
        if (!active) {
          return;
        }
        retryTimer = window.setTimeout(startConnection, 3000);
      }
    };

    startConnection();

    return () => {
      active = false;
      if (retryTimer) {
        window.clearTimeout(retryTimer);
      }
      void connection.stop();
    };
  }, [hubUrl, refreshActiveSet]);

  useEffect(() => {
    const points = activityQuery.data?.points ?? [];
    if (points.length === 0) {
      return;
    }
    const now = Date.now();
    points.forEach((point) => {
      if (point.count > 0) {
        lastEventByCameraRef.current.set(point.cameraId, now);
      }
    });
    refreshActiveSet();
  }, [activityQuery.data, refreshActiveSet]);

  useEffect(() => {
    const timer = window.setInterval(() => {
      refreshActiveSet();
    }, ALERT_SWEEP_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [refreshActiveSet]);

  useEffect(() => {
    if (!mapContainerRef.current) {
      return;
    }
    if (!activeMap || activeMap.type !== 'Geo' || !geoStyleUrl) {
      if (mapInstanceRef.current) {
        mapInstanceRef.current.remove();
        mapInstanceRef.current = null;
      }
      mapMarkersRef.current.forEach((marker) => marker.remove());
      mapMarkersRef.current.clear();
      return;
    }

    if (mapInstanceRef.current && lastMapIdRef.current === activeMap.id) {
      return;
    }

    if (mapInstanceRef.current) {
      mapInstanceRef.current.remove();
      mapInstanceRef.current = null;
    }
    mapMarkersRef.current.forEach((marker) => marker.remove());
    mapMarkersRef.current.clear();

    const center: [number, number] =
      activeMap.geoCenterLatitude != null && activeMap.geoCenterLongitude != null
        ? [activeMap.geoCenterLongitude, activeMap.geoCenterLatitude]
        : DEFAULT_CENTER;
    const zoom = activeMap.geoZoom ?? DEFAULT_ZOOM;

    const map = new maplibregl.Map({
      container: mapContainerRef.current,
      style: geoStyleUrl,
      center,
      zoom
    });

    map.addControl(new maplibregl.NavigationControl(), 'top-right');

    mapInstanceRef.current = map;
    lastMapIdRef.current = activeMap.id;
  }, [activeMap, geoStyleUrl]);

  const setImageViewportNode = (node: HTMLDivElement | null) => {
    imageViewportRef.current = node;
    const refAny: any = imageViewportSizeRef;
    if (typeof refAny === 'function') {
      refAny(node);
    } else if (refAny) {
      refAny.current = node;
    }
  };

  const resetImageView = () => {
    if (!imageNaturalSize.width || !imageNaturalSize.height) {
      return;
    }
    if (!viewportWidth || !viewportHeight) {
      return;
    }
    const scale = Math.min(
      viewportWidth / imageNaturalSize.width,
      viewportHeight / imageNaturalSize.height
    );
    const translateX = (viewportWidth - imageNaturalSize.width * scale) / 2;
    const translateY = (viewportHeight - imageNaturalSize.height * scale) / 2;
    setImageView({ scale, translateX, translateY });
  };

  useEffect(() => {
    if (!activeMap || activeMap.type !== 'Image') {
      return;
    }
    if (!imageNaturalSize.width || !imageNaturalSize.height) {
      return;
    }
    if (!viewportWidth || !viewportHeight) {
      return;
    }

    const shouldReset =
      lastImageMapIdRef.current !== activeMap.id || !imageViewInitializedRef.current;
    if (shouldReset) {
      resetImageView();
      imageViewInitializedRef.current = true;
    }
    lastImageMapIdRef.current = activeMap.id;
  }, [
    activeMap,
    imageNaturalSize.height,
    imageNaturalSize.width,
    viewportHeight,
    viewportWidth
  ]);

  const zoomImage = (factor: number, anchorX: number, anchorY: number) => {
    setImageView((prev) => {
      const nextScale = clamp(prev.scale * factor, MIN_IMAGE_ZOOM, MAX_IMAGE_ZOOM);
      const dx = (anchorX - prev.translateX) / prev.scale;
      const dy = (anchorY - prev.translateY) / prev.scale;
      const translateX = anchorX - dx * nextScale;
      const translateY = anchorY - dy * nextScale;
      return { scale: nextScale, translateX, translateY };
    });
  };

  const handleImageWheel = (event: React.WheelEvent<HTMLDivElement>) => {
    if (!activeMap || activeMap.type !== 'Image') {
      return;
    }
    if (!imageNaturalSize.width || !imageNaturalSize.height) {
      return;
    }
    event.preventDefault();
    const container = imageViewportRef.current;
    if (!container) {
      return;
    }
    const rect = container.getBoundingClientRect();
    const anchorX = event.clientX - rect.left;
    const anchorY = event.clientY - rect.top;
    const factor = event.deltaY < 0 ? 1.1 : 0.9;
    zoomImage(factor, anchorX, anchorY);
  };

  const handleImagePointerDown = (event: React.PointerEvent<HTMLDivElement>) => {
    if (!activeMap || activeMap.type !== 'Image') {
      return;
    }
    if (!activeMap.imageUrl) {
      return;
    }
    if (event.button !== 0) {
      return;
    }
    const target = event.target as HTMLElement;
    if (target.closest('[data-no-pan="true"]')) {
      return;
    }
    dragStateRef.current = {
      startX: event.clientX,
      startY: event.clientY,
      originX: imageView.translateX,
      originY: imageView.translateY
    };
    setIsPanning(true);
  };

  useEffect(() => {
    const handlePointerMove = (event: PointerEvent) => {
      const dragState = dragStateRef.current;
      if (!dragState) {
        return;
      }
      if (activeMap?.type !== 'Image') {
        return;
      }
      setImageView((prev) => ({
        ...prev,
        translateX: dragState.originX + (event.clientX - dragState.startX),
        translateY: dragState.originY + (event.clientY - dragState.startY)
      }));
    };

    const handlePointerUp = () => {
      if (dragStateRef.current) {
        dragStateRef.current = null;
        setIsPanning(false);
      }
    };

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', handlePointerUp);

    return () => {
      window.removeEventListener('pointermove', handlePointerMove);
      window.removeEventListener('pointerup', handlePointerUp);
    };
  }, [activeMap?.type]);

  useEffect(() => {
    const map = mapInstanceRef.current;
    if (!map || activeMap?.type !== 'Geo') {
      return;
    }

    const markerMap = mapMarkersRef.current;
    const nextIds = new Set<string>();

    mapPositions.forEach((position) => {
      if (position.latitude == null || position.longitude == null) {
        return;
      }
      const cameraId = position.cameraId;
      nextIds.add(cameraId);
      const label = cameraLabelById.get(cameraId) ?? position.label?.trim() ?? cameraId;
      const angle = position.angleDegrees ?? 0;
      const scale = position.iconScale ?? 1;
      const active = activeCameraIds.has(cameraId);

      const existing = markerMap.get(cameraId);
      if (existing) {
        existing.setLngLat([position.longitude, position.latitude]);
        updateCameraMarkerElement(existing.getElement(), {
          label,
          angle,
          scale,
          active
        });
        return;
      }

      const element = createCameraMarkerElement();
      updateCameraMarkerElement(element, { label, angle, scale, active });
      const marker = new maplibregl.Marker({ element, draggable: false })
        .setLngLat([position.longitude, position.latitude])
        .addTo(map);
      markerMap.set(cameraId, marker);
    });

    for (const [cameraId, marker] of markerMap.entries()) {
      if (!nextIds.has(cameraId)) {
        marker.remove();
        markerMap.delete(cameraId);
      }
    }
  }, [activeCameraIds, activeMap?.type, cameraLabelById, mapPositions]);

  const resolvedImageUrl = activeMap?.imageUrl ? buildApiUrl(activeMap.imageUrl) : null;
  const mapHasImage = Boolean(resolvedImageUrl);

  const imageMarkers = useMemo(() => {
    if (!activeMap || activeMap.type !== 'Image') {
      return [];
    }
    if (!imageNaturalSize.width || !imageNaturalSize.height) {
      return [];
    }
    return mapPositions
      .filter((position) => position.x != null && position.y != null)
      .map((position) => {
        const cameraId = position.cameraId;
        const label = cameraLabelById.get(cameraId) ?? position.label?.trim() ?? cameraId;
        const angle = position.angleDegrees ?? 0;
        const scale = position.iconScale ?? 1;
        const px = (position.x ?? 0) * imageNaturalSize.width;
        const py = (position.y ?? 0) * imageNaturalSize.height;
        return {
          cameraId,
          px,
          py,
          label,
          angle,
          scale,
          active: activeCameraIds.has(cameraId)
        };
      });
  }, [activeMap, activeCameraIds, cameraLabelById, imageNaturalSize, mapPositions]);

  const renderEmpty = (message: string) => (
    <Box
      h="100%"
      style={{
        borderRadius: 16,
        border: '1px solid var(--app-border)',
        background: 'linear-gradient(135deg, rgba(243,111,33,0.12), rgba(90,90,90,0.08))',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center'
      }}
    >
      <Stack gap={4} align="center">
        <IconMapPin size={32} />
        <Text size="sm" className="muted-text">
          {message}
        </Text>
      </Stack>
    </Box>
  );

  const renderImageMap = () => (
    <Box
      ref={setImageViewportNode}
      h="100%"
      onWheel={handleImageWheel}
      onPointerDown={handleImagePointerDown}
      style={{
        position: 'absolute',
        inset: 0,
        borderRadius: 16,
        border: '1px solid var(--app-border)',
        background: mapHasImage
          ? 'linear-gradient(135deg, rgba(24,24,24,0.95), rgba(24,24,24,0.65))'
          : 'linear-gradient(135deg, rgba(243,111,33,0.12), rgba(90,90,90,0.08))',
        overflow: 'hidden',
        cursor: mapHasImage ? (isPanning ? 'grabbing' : 'grab') : 'default',
        touchAction: 'none'
      }}
    >
      {!mapHasImage && (
        <Box
          h="100%"
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center'
          }}
        >
          <Stack gap={4} align="center">
            <IconMapPin size={32} />
            <Text size="sm" className="muted-text">
              {t('components.map.imageMissing')}
            </Text>
          </Stack>
        </Box>
      )}
      {mapHasImage && (
        <Box
          style={{
            position: 'absolute',
            left: 0,
            top: 0,
            width: imageNaturalSize.width,
            height: imageNaturalSize.height,
            transform: `translate(${imageView.translateX}px, ${imageView.translateY}px) scale(${imageView.scale})`,
            transformOrigin: 'top left'
          }}
        >
          <img
            src={resolvedImageUrl ?? ''}
            alt={activeMap?.name ?? 'map'}
            onLoad={(event) => {
              const image = event.currentTarget;
              if (image.naturalWidth && image.naturalHeight) {
                imageViewInitializedRef.current = false;
                setImageNaturalSize({
                  width: image.naturalWidth,
                  height: image.naturalHeight
                });
              }
            }}
            style={{
              width: imageNaturalSize.width || 'auto',
              height: imageNaturalSize.height || 'auto',
              display: 'block'
            }}
            draggable={false}
          />
          {imageMarkers.map((marker) => {
            const iconSize = Math.max(20, Math.round(BASE_ICON_SIZE * marker.scale));
            const ringSize = Math.round(iconSize * 1.9);
            return (
              <Box
                key={marker.cameraId}
                className="dashboard-map-marker"
                style={{
                  position: 'absolute',
                  left: marker.px,
                  top: marker.py
                }}
              >
                <Box
                  className={`dashboard-map-ring${marker.active ? ' is-active' : ''}`}
                  style={{ width: ringSize, height: ringSize }}
                />
                <Box
                  className="dashboard-map-icon"
                  style={{
                    width: iconSize,
                    height: iconSize,
                    transform: `translate(-50%, -50%) rotate(${normalizeAngle(
                      marker.angle + ICON_ROTATION_OFFSET
                    )}deg)`
                  }}
                />
                <Box
                  className="dashboard-map-label"
                  style={{ display: marker.label ? 'block' : 'none' }}
                >
                  {marker.label}
                </Box>
              </Box>
            );
          })}
        </Box>
      )}
    </Box>
  );

  return (
    <Paper p="md" radius="lg" className="surface-card" style={{ height: '100%', minHeight: 0 }}>
      <Box style={{ position: 'relative', height: '100%', minHeight: 0 }}>
        {!activeMap && renderEmpty(t('components.map.empty'))}
        {activeMap?.type === 'Image' && renderImageMap()}
        {activeMap?.type === 'Geo' && !geoStyleUrl && renderEmpty(t('components.map.geoStyleMissingShort'))}
        {activeMap?.type === 'Geo' && geoStyleUrl && (
          <Box
            ref={mapContainerRef}
            style={{
              position: 'absolute',
              inset: 0,
              borderRadius: 16,
              overflow: 'hidden'
            }}
          />
        )}
        <Box style={{ position: 'absolute', top: 12, left: 12, zIndex: 5 }}>
          <Paper
            p="xs"
            radius="md"
            className="surface-card"
            style={{ display: 'flex', gap: 8, alignItems: 'center' }}
          >
            <Select
              data={mapSelectOptions}
              value={selectedMapId}
              onChange={setSelectedMapId}
              placeholder={t('components.map.selectMap')}
              searchable
              style={{ width: 220 }}
            />
            <ActionIcon
              variant="light"
              onClick={() => mapsQuery.refetch()}
              aria-label={t('components.map.refreshMaps')}
            >
              <IconRefresh size={16} />
            </ActionIcon>
          </Paper>
        </Box>
      </Box>
    </Paper>
  );
}
