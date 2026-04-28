import { ActionIcon, Box, Group, Paper, Select, Stack, Text, Tooltip } from '@mantine/core';
import { useElementSize } from '@mantine/hooks';
import {
  IconFocus2,
  IconMapPin,
  IconRefresh,
  IconZoomIn,
  IconZoomOut
} from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import type { Feature, FeatureCollection, LineString, Point } from 'geojson';
import maplibregl, { type Map as MapLibreMap } from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
import { useEffect, useMemo, useRef, useState } from 'react';
import { buildApiUrl } from '../api/client';
import { buildMapRoute, getMap, getMapOptions, listCameras, listMaps } from '../api/ingestor';
import type {
  CameraResponse,
  FaceEventRecord,
  GeoPoint,
  MapCameraPositionResponse
} from '../api/types';
import { useI18n } from '../i18n/I18nProvider';
import { formatDateTime } from '../utils/format';

const MAP_TRACE_LIMIT = 50;
const MIN_IMAGE_ZOOM = 0.2;
const MAX_IMAGE_ZOOM = 6;
const MARKER_SIZE = 72;
const MARKER_BORDER = 3;
const TIME_BADGE_FONT = 12;
const USE_GEO_TRACE_OVERLAY = true;
const ACTIVE_RING_SCALE = 1.9;

const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));

type TracePoint = {
  cameraId: string;
  event: FaceEventRecord;
  x?: number;
  y?: number;
  latitude?: number;
  longitude?: number;
};

type DragState = {
  startX: number;
  startY: number;
  originX: number;
  originY: number;
};

const formatOptionalPersonName = (event: FaceEventRecord) => {
  const person = event.person;
  if (!person) {
    return null;
  }
  const parts = [person.firstName, person.lastName].filter(Boolean);
  const name = parts.join(' ').trim();
  return name || null;
};

const bestshotToUrl = (base64?: string | null) =>
  base64 ? `data:image/jpeg;base64,${base64}` : null;

type FaceTraceMapPanelProps = {
  results?: FaceEventRecord[];
  accentColor?: string;
  highlightLatest?: boolean;
  highlightEventId?: string | null;
};

const buildLiveViewUrl = (cameraId: string) => `/live-view?cameraId=${encodeURIComponent(cameraId)}`;

export function FaceTraceMapPanel({
  results,
  accentColor,
  highlightLatest = false,
  highlightEventId = null
}: FaceTraceMapPanelProps) {
  const { t } = useI18n();
  const accent = accentColor?.trim() || '#f36f21';
  const [selectedMapId, setSelectedMapId] = useState<string | null>(null);
  const [imageNaturalSize, setImageNaturalSize] = useState({ width: 0, height: 0 });
  const [imageView, setImageView] = useState({ scale: 1, translateX: 0, translateY: 0 });
  const [routePoints, setRoutePoints] = useState<GeoPoint[]>([]);
  const [isPanning, setIsPanning] = useState(false);
  const [geoProjectionVersion, setGeoProjectionVersion] = useState(0);
  const mapContainerRef = useRef<HTMLDivElement | null>(null);
  const mapInstanceRef = useRef<MapLibreMap | null>(null);
  const mapMarkersRef = useRef<Map<string, maplibregl.Marker>>(new Map());
  const imageViewportRef = useRef<HTMLDivElement | null>(null);
  const dragStateRef = useRef<DragState | null>(null);
  const lastImageMapIdRef = useRef<string | null>(null);
  const lastAppliedGeoMapIdRef = useRef<string | null>(null);
  const imageViewInitializedRef = useRef(false);
  const { ref: imageViewportSizeRef, width: viewportWidth, height: viewportHeight } =
    useElementSize();

  const mapsQuery = useQuery({ queryKey: ['maps'], queryFn: listMaps });
  const mapOptionsQuery = useQuery({ queryKey: ['maps', 'options'], queryFn: getMapOptions });
  const camerasQuery = useQuery({ queryKey: ['cameras'], queryFn: listCameras });

  useEffect(() => {
    if (!selectedMapId && mapsQuery.data?.length) {
      setSelectedMapId(mapsQuery.data[0].id);
    }
  }, [mapsQuery.data, selectedMapId]);

  const mapDetailQuery = useQuery({
    queryKey: ['maps', selectedMapId],
    queryFn: () => getMap(selectedMapId ?? ''),
    enabled: Boolean(selectedMapId)
  });

  const activeMap = mapDetailQuery.data?.map;
  const geoStyleUrl = mapOptionsQuery.data?.geoStyleUrl?.trim();
  const routingEnabled = mapOptionsQuery.data?.routingEnabled ?? false;
  const positions = useMemo<MapCameraPositionResponse[]>(
    () => mapDetailQuery.data?.cameras ?? [],
    [mapDetailQuery.data]
  );

  const positionsByCamera = useMemo(() => {
    return new Map(positions.map((position) => [position.cameraId, position]));
  }, [positions]);

  const traceEvents = results ?? [];

  const cameraCodeById = useMemo(() => {
    return new Map(
      (camerasQuery.data ?? []).map((camera: CameraResponse) => [
        camera.cameraId,
        camera.code ?? camera.cameraId
      ])
    );
  }, [camerasQuery.data]);

  const faceCountByCamera = useMemo(() => {
    const counts = new Map<string, number>();
    traceEvents.forEach((event) => {
      counts.set(event.cameraId, (counts.get(event.cameraId) ?? 0) + 1);
    });
    return counts;
  }, [traceEvents]);


  const mapSelectOptions = useMemo(
    () =>
      (mapsQuery.data ?? []).map((map) => ({
        value: map.id,
        label: `${map.name} (${map.type})`
      })),
    [mapsQuery.data]
  );

  const trackingPoints = useMemo<TracePoint[]>(() => {
    const points = traceEvents
      .map((event) => {
        const position = positionsByCamera.get(event.cameraId);
        if (!position) {
          return null;
        }
        return {
          cameraId: event.cameraId,
          event,
          x: position.x ?? undefined,
          y: position.y ?? undefined,
          latitude: position.latitude ?? undefined,
          longitude: position.longitude ?? undefined
        };
      })
      .filter(Boolean) as TracePoint[];

    points.sort(
      (a, b) =>
        new Date(a.event.eventTimeUtc).getTime() -
        new Date(b.event.eventTimeUtc).getTime()
    );

    return points.slice(0, MAP_TRACE_LIMIT);
  }, [positionsByCamera, traceEvents]);

  const highlightId = useMemo(() => {
    if (highlightEventId) {
      return highlightEventId;
    }
    if (!highlightLatest) {
      return null;
    }
    return trackingPoints.length ? trackingPoints[trackingPoints.length - 1].event.id : null;
  }, [highlightEventId, highlightLatest, trackingPoints]);

  const imageTracePoints = useMemo(() => {
    if (!activeMap || activeMap.type !== 'Image') {
      return [];
    }
    if (imageNaturalSize.width <= 0 || imageNaturalSize.height <= 0) {
      return [];
    }
    return trackingPoints
      .filter((point) => point.x !== undefined && point.y !== undefined)
      .map((point) => ({
        ...point,
        px: (point.x ?? 0) * imageNaturalSize.width,
        py: (point.y ?? 0) * imageNaturalSize.height
      }));
  }, [activeMap, imageNaturalSize.height, imageNaturalSize.width, trackingPoints]);

  const geoTracePoints = useMemo(() => {
    if (!USE_GEO_TRACE_OVERLAY || activeMap?.type !== 'Geo') {
      return [];
    }
    const map = mapInstanceRef.current;
    if (!map || !map.isStyleLoaded()) {
      return [];
    }
    return trackingPoints
      .filter((point) => point.latitude !== undefined && point.longitude !== undefined)
      .map((point) => {
        const projected = map.project([point.longitude ?? 0, point.latitude ?? 0]);
        return {
          ...point,
          px: projected.x,
          py: projected.y
        };
      });
  }, [activeMap?.type, geoProjectionVersion, trackingPoints]);

  const geoRoutePixels = useMemo(() => {
    if (!USE_GEO_TRACE_OVERLAY || activeMap?.type !== 'Geo') {
      return [];
    }
    const map = mapInstanceRef.current;
    if (!map || !map.isStyleLoaded()) {
      return [];
    }
    return routePoints.map((point) => {
      const projected = map.project([point.longitude, point.latitude]);
      return { x: projected.x, y: projected.y };
    });
  }, [activeMap?.type, geoProjectionVersion, routePoints]);

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

    const shouldReset = lastImageMapIdRef.current !== activeMap.id || !imageViewInitializedRef.current;
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
    if (activeMap?.type !== 'Geo' || trackingPoints.length < 2) {
      setRoutePoints([]);
      return;
    }

    const geoPoints = trackingPoints
      .filter((point) => point.latitude !== undefined && point.longitude !== undefined)
      .map((point) => ({
        latitude: point.latitude ?? 0,
        longitude: point.longitude ?? 0
      }));

    if (geoPoints.length < 2) {
      setRoutePoints([]);
      return;
    }

    if (!routingEnabled) {
      setRoutePoints(geoPoints);
      return;
    }

    buildMapRoute({ points: geoPoints })
      .then((response) => {
        setRoutePoints(response.points);
      })
      .catch(() => {
        setRoutePoints(geoPoints);
      });
  }, [activeMap?.type, routingEnabled, trackingPoints]);

  useEffect(() => {
    if (activeMap?.type !== 'Geo' || !geoStyleUrl || !mapContainerRef.current) {
      if (mapInstanceRef.current) {
        mapInstanceRef.current.remove();
        mapInstanceRef.current = null;
        mapMarkersRef.current.forEach((marker) => marker.remove());
        mapMarkersRef.current.clear();
      }
      return;
    }

    if (mapInstanceRef.current) {
      return;
    }

    const savedCenter =
      activeMap?.geoCenterLatitude != null && activeMap?.geoCenterLongitude != null
        ? ([activeMap.geoCenterLongitude, activeMap.geoCenterLatitude] as [number, number])
        : null;
    const savedZoom = activeMap?.geoZoom ?? null;

    const initial = positions.find(
      (position) => position.latitude != null && position.longitude != null
    );
    const center: [number, number] = initial
      ? [initial.longitude ?? 0, initial.latitude ?? 0]
      : [106.7, 10.8];

    const map = new maplibregl.Map({
      container: mapContainerRef.current,
      style: geoStyleUrl,
      center: savedCenter ?? center,
      zoom: savedZoom ?? (initial ? 17 : 11)
    });

    map.addControl(new maplibregl.NavigationControl(), 'top-right');
    map.on('load', () => {
      if (USE_GEO_TRACE_OVERLAY) {
        setGeoProjectionVersion((prev) => (prev + 1) % 1000000);
        return;
      }
      map.addSource('trace-line', {
        type: 'geojson',
        data: {
          type: 'Feature',
          properties: {},
          geometry: {
            type: 'LineString',
            coordinates: []
          }
        }
      });
      map.addLayer({
        id: 'trace-line',
        type: 'line',
        source: 'trace-line',
        paint: {
          'line-color': accent,
          'line-width': 3
        }
      });
      map.addSource('trace-points', {
        type: 'geojson',
        data: {
          type: 'FeatureCollection',
          features: []
        }
      });
      map.addLayer({
        id: 'trace-points',
        type: 'circle',
        source: 'trace-points',
        paint: {
          'circle-radius': 5,
          'circle-color': accent,
          'circle-stroke-width': 2,
          'circle-stroke-color': '#1f1f1f'
        }
      });
    });

    if (USE_GEO_TRACE_OVERLAY && map.isStyleLoaded()) {
      setGeoProjectionVersion((prev) => (prev + 1) % 1000000);
    }

    mapInstanceRef.current = map;
  }, [activeMap?.type, accent, geoStyleUrl, positions]);

  useEffect(() => {
    const map = mapInstanceRef.current;
    if (!map || activeMap?.type !== 'Geo' || !USE_GEO_TRACE_OVERLAY) {
      return;
    }

    let rafId = 0;
    const bumpProjection = () => {
      rafId = 0;
      setGeoProjectionVersion((prev) => (prev + 1) % 1000000);
    };
    const scheduleProjection = () => {
      if (rafId) {
        return;
      }
      rafId = window.requestAnimationFrame(bumpProjection);
    };

    map.on('move', scheduleProjection);
    map.on('zoom', scheduleProjection);
    map.on('resize', scheduleProjection);
    map.on('load', scheduleProjection);
    scheduleProjection();

    return () => {
      map.off('move', scheduleProjection);
      map.off('zoom', scheduleProjection);
      map.off('resize', scheduleProjection);
      map.off('load', scheduleProjection);
      if (rafId) {
        window.cancelAnimationFrame(rafId);
      }
    };
  }, [activeMap?.type, activeMap?.id]);

  useEffect(() => {
    const map = mapInstanceRef.current;
    const mapId = mapDetailQuery.data?.map.id;
    if (!map || !mapId || activeMap?.type !== 'Geo') {
      return;
    }
    if (lastAppliedGeoMapIdRef.current === mapId) {
      return;
    }

    const storedLat = mapDetailQuery.data?.map.geoCenterLatitude;
    const storedLng = mapDetailQuery.data?.map.geoCenterLongitude;
    const storedZoom = mapDetailQuery.data?.map.geoZoom;
    const savedCenter =
      storedLat != null && storedLng != null ? ([storedLng, storedLat] as [number, number]) : null;

    const initial = mapDetailQuery.data?.cameras.find(
      (position) => position.latitude != null && position.longitude != null
    );
    const fallbackCenter: [number, number] = initial
      ? [initial.longitude ?? 0, initial.latitude ?? 0]
      : [106.7, 10.8];
    const fallbackZoom = initial ? 17 : 11;

    map.jumpTo({
      center: savedCenter ?? fallbackCenter,
      zoom: storedZoom ?? fallbackZoom
    });

    lastAppliedGeoMapIdRef.current = mapId;
  }, [activeMap?.type, mapDetailQuery.data?.map.id]);

  useEffect(() => {
    const map = mapInstanceRef.current;
    if (USE_GEO_TRACE_OVERLAY) {
      return;
    }
    if (!map) {
      return;
    }
    const source = map.getSource('trace-line') as maplibregl.GeoJSONSource | undefined;
    const coords = routePoints.map((point) => [point.longitude, point.latitude]);
    if (source) {
      const lineData: Feature<LineString, Record<string, unknown>> = {
        type: 'Feature',
        properties: {},
        geometry: {
          type: 'LineString',
          coordinates: coords
        }
      };
      source.setData(lineData);
    }
  }, [routePoints]);

  useEffect(() => {
    const map = mapInstanceRef.current;
    if (USE_GEO_TRACE_OVERLAY) {
      return;
    }
    if (!map) {
      return;
    }
    const source = map.getSource('trace-points') as maplibregl.GeoJSONSource | undefined;
    if (!source) {
      return;
    }
    const features: Feature<Point, { cameraId: string }>[] = trackingPoints
      .filter((point) => point.latitude !== undefined && point.longitude !== undefined)
      .map((point) => ({
        type: 'Feature',
        properties: {
          cameraId: point.cameraId
        },
        geometry: {
          type: 'Point',
          coordinates: [point.longitude ?? 0, point.latitude ?? 0]
        }
      }));
    const collection: FeatureCollection<Point, { cameraId: string }> = {
      type: 'FeatureCollection',
      features
    };
    source.setData(collection);
  }, [trackingPoints]);

  useEffect(() => {
    const map = mapInstanceRef.current;
    if (!map || activeMap?.type !== 'Geo') {
      return;
    }

    if (USE_GEO_TRACE_OVERLAY) {
      mapMarkersRef.current.forEach((marker) => marker.remove());
      mapMarkersRef.current.clear();
      return;
    }

    const markerMap = mapMarkersRef.current;
    const nextIds = new Set<string>();
    trackingPoints.forEach((point) => {
      if (point.latitude == null || point.longitude == null) {
        return;
      }
      const markerKey = point.event.id;
      nextIds.add(markerKey);
      const isActive = markerKey === highlightId;
      const existing = markerMap.get(markerKey);
      const imageUrl = bestshotToUrl(point.event.bestshotBase64);
      const cameraLabel = cameraCodeById.get(point.cameraId) ?? point.cameraId;
      const timeLabel = formatDateTime(point.event.eventTimeUtc, t);
      const personName = formatOptionalPersonName(point.event);
      const faceCount = faceCountByCamera.get(point.cameraId) ?? 1;
      const ringSize = Math.round(MARKER_SIZE * ACTIVE_RING_SCALE);

      const element = document.createElement('div');
      element.style.position = 'relative';
      element.style.display = 'flex';
      element.style.flexDirection = 'column';
      element.style.alignItems = 'center';
      element.style.gap = '6px';
      element.style.cursor = 'pointer';

      const thumb = document.createElement('div');
      thumb.style.width = `${MARKER_SIZE}px`;
      thumb.style.height = `${MARKER_SIZE}px`;
      thumb.style.borderRadius = '50%';
      thumb.style.border = `${MARKER_BORDER}px solid ${accent}`;
      thumb.style.boxShadow = '0 2px 8px rgba(0,0,0,0.5)';
      thumb.style.background = imageUrl
        ? `url(${imageUrl}) center / cover no-repeat`
        : accent;

      const timeBadge = document.createElement('div');
      timeBadge.textContent = timeLabel;
      timeBadge.style.padding = '2px 8px';
      timeBadge.style.borderRadius = '999px';
      timeBadge.style.background = '#ffffff';
      timeBadge.style.color = '#111111';
      timeBadge.style.border = '1px solid rgba(0, 0, 0, 0.12)';
      timeBadge.style.fontSize = `${TIME_BADGE_FONT}px`;
      timeBadge.style.fontWeight = '600';
      timeBadge.style.whiteSpace = 'nowrap';

      const tooltip = document.createElement('div');
      tooltip.style.position = 'absolute';
      tooltip.style.bottom = `${MARKER_SIZE + 22}px`;
      tooltip.style.left = '50%';
      tooltip.style.transform = 'translateX(-50%)';
      tooltip.style.minWidth = '180px';
      tooltip.style.padding = '12px';
      tooltip.style.borderRadius = '12px';
      tooltip.style.background = '#ffffff';
      tooltip.style.color = '#111111';
      tooltip.style.border = '1px solid rgba(0, 0, 0, 0.08)';
      tooltip.style.boxShadow = '0 6px 14px rgba(0,0,0,0.18)';
      tooltip.style.fontSize = '11px';
      tooltip.style.pointerEvents = 'none';
      tooltip.style.display = 'none';
      tooltip.style.zIndex = '2';

      const preview = document.createElement('div');
      preview.style.width = '140px';
      preview.style.height = '140px';
      preview.style.borderRadius = '12px';
      preview.style.marginBottom = '10px';
      preview.style.background = imageUrl
        ? `url(${imageUrl}) center / cover no-repeat`
        : accent;
      preview.style.border = '1px solid rgba(0, 0, 0, 0.08)';
      tooltip.appendChild(preview);

      const appendRow = (label: string, value: string) => {
        const row = document.createElement('div');
        row.style.display = 'flex';
        row.style.justifyContent = 'space-between';
        row.style.gap = '10px';
        const labelSpan = document.createElement('span');
        labelSpan.style.color = '#6b6b6b';
        labelSpan.textContent = label;
        const valueSpan = document.createElement('span');
        valueSpan.style.color = '#111111';
        valueSpan.style.fontWeight = '600';
        valueSpan.textContent = value;
        row.appendChild(labelSpan);
        row.appendChild(valueSpan);
        tooltip.appendChild(row);
      };

      appendRow(t('common.fields.camera'), cameraLabel);
      appendRow(t('common.fields.time'), timeLabel);
      if (personName) {
        appendRow(t('common.fields.name'), personName);
      }
      appendRow(t('common.fields.faces'), String(faceCount));

      const markerCore = document.createElement('div');
      markerCore.style.position = 'relative';
      markerCore.style.width = `${MARKER_SIZE}px`;
      markerCore.style.height = `${MARKER_SIZE}px`;

      if (isActive) {
        const ring = document.createElement('div');
        ring.className = 'dashboard-map-ring is-active';
        ring.style.width = `${ringSize}px`;
        ring.style.height = `${ringSize}px`;
        ring.style.left = '50%';
        ring.style.top = '50%';
        markerCore.appendChild(ring);
      }

      markerCore.appendChild(thumb);

      element.appendChild(tooltip);
      element.appendChild(markerCore);
      element.appendChild(timeBadge);

      element.onmouseenter = () => {
        tooltip.style.display = 'block';
      };
      element.onmouseleave = () => {
        tooltip.style.display = 'none';
      };
      element.onclick = () => {
        window.dispatchEvent(
          new CustomEvent('open-liveview', { detail: { cameraId: point.cameraId } })
        );
        window.open(buildLiveViewUrl(point.cameraId), '_blank');
      };

      if (existing) {
        existing.remove();
        markerMap.delete(markerKey);
      }
      const marker = new maplibregl.Marker({ element })
        .setLngLat([point.longitude, point.latitude])
        .addTo(map);
      markerMap.set(markerKey, marker);
    });

    for (const [markerId, marker] of markerMap.entries()) {
      if (!nextIds.has(markerId)) {
        marker.remove();
        markerMap.delete(markerId);
      }
    }
  }, [activeMap?.type, accent, cameraCodeById, faceCountByCamera, highlightId, trackingPoints]);

  const resolvedImageUrl = activeMap?.imageUrl ? buildApiUrl(activeMap.imageUrl) : null;
  const mapHasImage = Boolean(resolvedImageUrl);
  const renderTraceMarker = (point: TracePoint & { px: number; py: number }, index: number) => {
    const personName = formatOptionalPersonName(point.event);
    const cameraLabel = cameraCodeById.get(point.cameraId) ?? point.cameraId;
    const timeLabel = formatDateTime(point.event.eventTimeUtc, t);
    const faceCount = faceCountByCamera.get(point.cameraId) ?? 1;
    const previewUrl = bestshotToUrl(point.event.bestshotBase64);
    const isActive = point.event.id === highlightId;
    const ringSize = Math.round(MARKER_SIZE * ACTIVE_RING_SCALE);

    return (
      <Tooltip
        key={`${point.event.id}-${index}`}
        label={
          <Stack gap={6}>
            <Box
              style={{
                width: 120,
                height: 120,
                borderRadius: 10,
                border: '1px solid rgba(0, 0, 0, 0.08)',
                background: previewUrl
                  ? `url(${previewUrl}) center / cover no-repeat`
                  : '#f36f21'
              }}
            />
            <Group justify="space-between" align="center" gap="xs">
              <Text size="xs" className="muted-text">
                {t('common.fields.camera')}
              </Text>
              <Text size="xs" fw={600}>
                {cameraLabel}
              </Text>
            </Group>
            <Group justify="space-between" align="center" gap="xs">
              <Text size="xs" className="muted-text">
                {t('common.fields.time')}
              </Text>
              <Text size="xs" fw={600}>
                {timeLabel}
              </Text>
            </Group>
            {personName && (
              <Group justify="space-between" align="center" gap="xs">
                <Text size="xs" className="muted-text">
                  {t('common.fields.name')}
                </Text>
                <Text size="xs" fw={600}>
                  {personName}
                </Text>
              </Group>
            )}
            <Group justify="space-between" align="center" gap="xs">
              <Text size="xs" className="muted-text">
                {t('common.fields.faces')}
              </Text>
              <Text size="xs" fw={600}>
                {faceCount}
              </Text>
            </Group>
          </Stack>
        }
        withArrow
        styles={{
          tooltip: {
            backgroundColor: '#ffffff',
            color: '#111111',
            border: '1px solid rgba(0, 0, 0, 0.08)',
            boxShadow: '0 6px 14px rgba(0,0,0,0.18)',
            padding: 12
          },
          arrow: {
            borderColor: '#ffffff'
          }
        }}
      >
        <Box
          data-no-pan="true"
          style={{
            position: 'absolute',
            left: point.px,
            top: point.py,
            transform: 'translate(-50%, -50%)',
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            gap: 6,
            cursor: 'pointer',
            pointerEvents: 'auto'
          }}
          onClick={() => {
            window.dispatchEvent(
              new CustomEvent('open-liveview', { detail: { cameraId: point.cameraId } })
            );
            window.open(buildLiveViewUrl(point.cameraId), '_blank');
          }}
        >
          <Box style={{ position: 'relative', width: MARKER_SIZE, height: MARKER_SIZE }}>
            {isActive && (
              <Box
                className="dashboard-map-ring is-active"
                style={{
                  width: ringSize,
                  height: ringSize,
                  left: '50%',
                  top: '50%'
                }}
              />
            )}
            <Box
              style={{
                position: 'relative',
                zIndex: 1,
                width: MARKER_SIZE,
                height: MARKER_SIZE,
                borderRadius: '50%',
                border: `${MARKER_BORDER}px solid ${accent}`,
                background: previewUrl
                  ? `url(${previewUrl}) center / cover no-repeat`
                  : accent,
                boxShadow: '0 2px 8px rgba(0,0,0,0.5)'
              }}
            />
          </Box>
          <Box
            style={{
              padding: '2px 8px',
              borderRadius: 999,
              background: '#ffffff',
              color: '#111111',
              border: '1px solid rgba(0, 0, 0, 0.12)',
              fontSize: TIME_BADGE_FONT,
              fontWeight: 600,
              whiteSpace: 'nowrap'
            }}
          >
            {timeLabel}
          </Box>
        </Box>
      </Tooltip>
    );
  };

  const renderEmptyMap = (message: string) => (
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
        position: 'relative',
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
        <Group h="100%" align="center" justify="center">
          <Stack gap={4} align="center">
            <IconMapPin size={32} />
            <Text size="sm" className="muted-text">
              {t('components.map.imageMissing')}
            </Text>
          </Stack>
        </Group>
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
          <svg
            width={imageNaturalSize.width}
            height={imageNaturalSize.height}
            style={{
              position: 'absolute',
              inset: 0,
              pointerEvents: 'none'
            }}
          >
            {imageTracePoints.length > 1 && (
              <polyline
                points={imageTracePoints.map((point) => `${point.px},${point.py}`).join(' ')}
                fill="none"
                stroke={accent}
                strokeWidth="3"
                strokeLinecap="round"
                strokeLinejoin="round"
                opacity="0.9"
              />
            )}
          </svg>
          {imageTracePoints.map((point, index) => renderTraceMarker(point, index))}
        </Box>
      )}
    </Box>
  );

  const renderGeoMap = () => (
    <Box
      h="100%"
      style={{
        position: 'relative',
        borderRadius: 16,
        border: '1px solid var(--app-border)',
        overflow: 'hidden',
        background: geoStyleUrl ? '#111' : 'transparent'
      }}
    >
      <Box
        ref={mapContainerRef}
        style={{
          position: 'absolute',
          inset: 0
        }}
      />
      {USE_GEO_TRACE_OVERLAY && activeMap?.type === 'Geo' && geoStyleUrl && (
        <Box
          style={{
            position: 'absolute',
            inset: 0,
            pointerEvents: 'none'
          }}
        >
          <svg
            width="100%"
            height="100%"
            style={{
              position: 'absolute',
              inset: 0,
              pointerEvents: 'none'
            }}
          >
            {geoRoutePixels.length > 1 && (
              <polyline
                points={geoRoutePixels.map((point) => `${point.x},${point.y}`).join(' ')}
                fill="none"
                stroke={accent}
                strokeWidth="3"
                strokeLinecap="round"
                strokeLinejoin="round"
                opacity="0.9"
              />
            )}
          </svg>
          {geoTracePoints.map((point, index) => renderTraceMarker(point, index))}
        </Box>
      )}
      {!geoStyleUrl && (
        <Group h="100%" align="center" justify="center">
          <Stack gap={4} align="center">
            <IconMapPin size={32} />
            <Text size="sm" className="muted-text">
              {t('components.map.geoStyleMissing')}
            </Text>
          </Stack>
        </Group>
      )}
    </Box>
  );

  const renderMapPanel = () => {
    if (!activeMap) {
      return renderEmptyMap(t('components.map.empty'));
    }

    return activeMap.type === 'Geo' ? renderGeoMap() : renderImageMap();
  };

  const mapControls = (
    <Paper
      p="xs"
      radius="md"
      className="surface-card"
      style={{ display: 'flex', gap: 8, alignItems: 'center' }}
      data-no-pan="true"
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
  );

  const zoomControls =
    activeMap?.type === 'Image' ? (
      <Paper
        p="xs"
        radius="md"
        className="surface-card"
        style={{ display: 'flex', gap: 8, alignItems: 'center' }}
        data-no-pan="true"
      >
        <ActionIcon
          variant="light"
          onClick={() => zoomImage(1.1, viewportWidth / 2, viewportHeight / 2)}
          aria-label={t('components.map.zoomIn')}
        >
          <IconZoomIn size={16} />
        </ActionIcon>
        <ActionIcon
          variant="light"
          onClick={() => zoomImage(0.9, viewportWidth / 2, viewportHeight / 2)}
          aria-label={t('components.map.zoomOut')}
        >
          <IconZoomOut size={16} />
        </ActionIcon>
        <ActionIcon
          variant="light"
          onClick={resetImageView}
          aria-label={t('components.map.resetView')}
        >
          <IconFocus2 size={16} />
        </ActionIcon>
      </Paper>
    ) : null;

  return (
    <Paper
      p="md"
      radius="lg"
      className="surface-card"
      style={{ height: '100%', minHeight: 0 }}
    >
      <Box style={{ position: 'relative', height: '100%', minHeight: 0 }}>
        {renderMapPanel()}
        <Box style={{ position: 'absolute', top: 12, left: 12, zIndex: 5 }}>
          {mapControls}
        </Box>
        {zoomControls && (
          <Box style={{ position: 'absolute', top: 12, right: 12, zIndex: 5 }}>
            {zoomControls}
          </Box>
        )}
      </Box>
    </Paper>
  );
}
