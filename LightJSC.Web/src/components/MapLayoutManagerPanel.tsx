import {
  ActionIcon,
  Badge,
  Box,
  Button,
  Divider,
  FileInput,
  Group,
  Modal,
  Paper,
  ScrollArea,
  Select,
  Stack,
  Text,
  TextInput
} from '@mantine/core';
import { useDisclosure, useElementSize } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import {
  IconEdit,
  IconFilter,
  IconFocus2,
  IconMapPin,
  IconPlus,
  IconRefresh,
  IconSearch,
  IconTrash,
  IconX,
  IconUpload,
  IconZoomIn,
  IconZoomOut
} from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import maplibregl, { type Map as MapLibreMap } from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
import { useEffect, useMemo, useRef, useState, type CSSProperties, type DragEvent } from 'react';
import { buildApiUrl } from '../api/client';
import {
  createMap,
  deleteMap,
  getMap,
  getMapOptions,
  listMaps,
  saveMapCameras,
  updateMap,
  updateMapView,
  uploadMapImage
} from '../api/ingestor';
import type {
  CameraResponse,
  MapCameraPositionRequest,
  MapLayoutRequest,
  MapLayoutResponse,
  MapLayoutType,
  MapViewRequest
} from '../api/types';
import { useI18n } from '../i18n/I18nProvider';

const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));
const clamp01 = (value: number) => clamp(value, 0, 1);
const normalizeAngle = (value: number) => {
  const normalized = value % 360;
  return normalized < 0 ? normalized + 360 : normalized;
};

const DEFAULT_ANGLE_DEGREES = 0;
const DEFAULT_RANGE_IMAGE = 0.25;
const DEFAULT_RANGE_GEO = 25;
const DEFAULT_ICON_SCALE = 1;
const DEFAULT_FOV_DEGREES = 60;
const MIN_FOV_DEGREES = 5;
const MAX_FOV_DEGREES = 180;
const BASE_ICON_SIZE = 32;
const ICON_ROTATION_OFFSET = -90;
const MIN_ICON_SCALE = 0.2;
const MAX_ICON_SCALE = 5;
const MIN_IMAGE_ZOOM = 0.2;
const MAX_IMAGE_ZOOM = 6;
const HANDLE_SIZE = 10;
const ROTATE_HANDLE_OFFSET = 18;
const GEO_MARKER_BASE_ZOOM = 17;
const GEO_MARKER_SCALE_FACTOR = 0.28;
const GEO_MARKER_MIN_SCALE = 0.55;
const GEO_MARKER_MAX_SCALE = 2.4;

const toRadians = (value: number) => (value * Math.PI) / 180;
const clampFov = (value: number) => clamp(value, MIN_FOV_DEGREES, MAX_FOV_DEGREES);
const getGeoMarkerScale = (zoom: number) =>
  clamp(
    Math.pow(2, (zoom - GEO_MARKER_BASE_ZOOM) * GEO_MARKER_SCALE_FACTOR),
    GEO_MARKER_MIN_SCALE,
    GEO_MARKER_MAX_SCALE
  );
const EARTH_RADIUS_METERS = 6378137;
const getGeoBearing = (
  fromLat: number,
  fromLng: number,
  toLat: number,
  toLng: number
) => {
  const phi1 = toRadians(fromLat);
  const phi2 = toRadians(toLat);
  const deltaLng = toRadians(toLng - fromLng);
  const y = Math.sin(deltaLng) * Math.cos(phi2);
  const x =
    Math.cos(phi1) * Math.sin(phi2) -
    Math.sin(phi1) * Math.cos(phi2) * Math.cos(deltaLng);
  return normalizeAngle((Math.atan2(y, x) * 180) / Math.PI);
};
const getGeoDistance = (
  fromLat: number,
  fromLng: number,
  toLat: number,
  toLng: number
) => {
  const phi1 = toRadians(fromLat);
  const phi2 = toRadians(toLat);
  const deltaLat = toRadians(toLat - fromLat);
  const deltaLng = toRadians(toLng - fromLng);
  const a =
    Math.sin(deltaLat / 2) * Math.sin(deltaLat / 2) +
    Math.cos(phi1) * Math.cos(phi2) *
      Math.sin(deltaLng / 2) * Math.sin(deltaLng / 2);
  const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  return EARTH_RADIUS_METERS * c;
};
const angleDistance = (from: number, to: number) => {
  const diff = Math.abs(normalizeAngle(to - from));
  return diff > 180 ? 360 - diff : diff;
};
const meanAngle = (first: number, second: number) => {
  const a = toRadians(first);
  const b = toRadians(second);
  const x = Math.cos(a) + Math.cos(b);
  const y = Math.sin(a) + Math.sin(b);
  if (Math.abs(x) < 1e-6 && Math.abs(y) < 1e-6) {
    return normalizeAngle(first);
  }
  return normalizeAngle((Math.atan2(y, x) * 180) / Math.PI);
};

const buildImageSectorPoints = (
  cx: number,
  cy: number,
  angleDegrees: number,
  rangePixels: number,
  fovDegrees: number
) => {
  const bearing = toRadians(angleDegrees);
  const half = toRadians(fovDegrees / 2);
  const left = bearing - half;
  const right = bearing + half;
  const leftX = cx + rangePixels * Math.sin(left);
  const leftY = cy - rangePixels * Math.cos(left);
  const rightX = cx + rangePixels * Math.sin(right);
  const rightY = cy - rangePixels * Math.cos(right);
  return `${cx},${cy} ${leftX},${leftY} ${rightX},${rightY}`;
};

const getImageSectorEdges = (
  cx: number,
  cy: number,
  angleDegrees: number,
  rangePixels: number,
  fovDegrees: number
) => {
  const bearing = toRadians(angleDegrees);
  const half = toRadians(fovDegrees / 2);
  const left = bearing - half;
  const right = bearing + half;
  return {
    left: {
      x: cx + rangePixels * Math.sin(left),
      y: cy - rangePixels * Math.cos(left)
    },
    right: {
      x: cx + rangePixels * Math.sin(right),
      y: cy - rangePixels * Math.cos(right)
    }
  };
};

const getBearingDegrees = (dx: number, dy: number) => {
  const radians = Math.atan2(dx, -dy);
  return normalizeAngle((radians * 180) / Math.PI);
};

const destinationPoint = (
  latitude: number,
  longitude: number,
  bearingDegrees: number,
  distanceMeters: number
) => {
  const radius = 6378137;
  const bearing = toRadians(bearingDegrees);
  const delta = distanceMeters / radius;
  const phi1 = toRadians(latitude);
  const lambda1 = toRadians(longitude);

  const sinPhi1 = Math.sin(phi1);
  const cosPhi1 = Math.cos(phi1);
  const sinDelta = Math.sin(delta);
  const cosDelta = Math.cos(delta);

  const phi2 = Math.asin(sinPhi1 * cosDelta + cosPhi1 * sinDelta * Math.cos(bearing));
  const lambda2 =
    lambda1 +
    Math.atan2(
      Math.sin(bearing) * sinDelta * cosPhi1,
      cosDelta - sinPhi1 * Math.sin(phi2)
    );

  return {
    latitude: (phi2 * 180) / Math.PI,
    longitude: (lambda2 * 180) / Math.PI
  };
};

const toPositionRequest = (position: MapCameraPositionRequest) => ({
  cameraId: position.cameraId,
  label: position.label,
  x: position.x,
  y: position.y,
  angleDegrees: position.angleDegrees,
  fovDegrees: position.fovDegrees,
  range: position.range,
  iconScale: position.iconScale,
  latitude: position.latitude,
  longitude: position.longitude
});

type DragState =
  | {
      kind: 'pan';
      startX: number;
      startY: number;
      originX: number;
      originY: number;
    }
  | {
      kind: 'camera';
      cameraId: string;
      offsetX: number;
      offsetY: number;
    }
  | {
      kind: 'fov';
      cameraId: string;
      side: 'left' | 'right';
    }
  | {
      kind: 'rotate';
      cameraId: string;
    }
  | {
      kind: 'scale';
      cameraId: string;
      startScale: number;
      startDistance: number;
    };

type GeoDragState =
  | {
      kind: 'camera';
      cameraId: string;
      offsetX: number;
      offsetY: number;
    }
  | {
      kind: 'fov';
      cameraId: string;
      side: 'left' | 'right';
    }
  | {
      kind: 'rotate';
      cameraId: string;
    }
  | {
      kind: 'scale';
      cameraId: string;
      startScale: number;
      startDistance: number;
    };

type MapTreeNode = {
  map: MapLayoutResponse;
  children: MapTreeNode[];
};

const buildMapTree = (maps: MapLayoutResponse[]) => {
  const nodes = new Map<string, MapTreeNode>();
  maps.forEach((map) => {
    nodes.set(map.id, { map, children: [] });
  });

  const roots: MapTreeNode[] = [];
  nodes.forEach((node) => {
    if (node.map.parentId && nodes.has(node.map.parentId)) {
      nodes.get(node.map.parentId)?.children.push(node);
    } else {
      roots.push(node);
    }
  });

  const sortNodes = (items: MapTreeNode[]) => {
    items.sort((a, b) => a.map.name.localeCompare(b.map.name));
    items.forEach((item) => sortNodes(item.children));
  };
  sortNodes(roots);

  return roots;
};

const flattenMapTree = (
  nodes: MapTreeNode[],
  depth = 0,
  excludeId?: string
): { value: string; label: string }[] => {
  const options: { value: string; label: string }[] = [];
  nodes.forEach((node) => {
    if (node.map.id !== excludeId) {
      const prefix = depth > 0 ? `${'--'.repeat(depth)} ` : '';
      options.push({ value: node.map.id, label: `${prefix}${node.map.name}` });
    }
    options.push(...flattenMapTree(node.children, depth + 1, excludeId));
  });
  return options;
};

export function MapLayoutManagerPanel({ cameras }: { cameras: CameraResponse[] }) {
  const queryClient = useQueryClient();
  const { t } = useI18n();
  const [selectedMapId, setSelectedMapId] = useState<string | null>(null);
  const [positions, setPositions] = useState<MapCameraPositionRequest[]>([]);
  const [positionsDirty, setPositionsDirty] = useState(false);
  const [selectedCameraId, setSelectedCameraId] = useState<string | null>(null);
  const [createOpened, createHandlers] = useDisclosure(false);
  const [editOpened, editHandlers] = useDisclosure(false);
  const [deleteOpened, deleteHandlers] = useDisclosure(false);
  const [newMapName, setNewMapName] = useState('');
  const [newMapType, setNewMapType] = useState<MapLayoutType>('Image');
  const [newMapParentId, setNewMapParentId] = useState<string | null>(null);
  const [editMap, setEditMap] = useState<MapLayoutResponse | null>(null);
  const [editMapName, setEditMapName] = useState('');
  const [editMapType, setEditMapType] = useState<MapLayoutType>('Image');
  const [editMapParentId, setEditMapParentId] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<MapLayoutResponse | null>(null);
  const [mapSearch, setMapSearch] = useState('');
  const [search, setSearch] = useState('');
  const [isFullscreen, setIsFullscreen] = useState(false);
  const mapContainerRef = useRef<HTMLDivElement | null>(null);
  const mapInstanceRef = useRef<MapLibreMap | null>(null);
  const mapMarkersRef = useRef<Map<string, maplibregl.Marker>>(new Map());
  const selectedCameraIdRef = useRef<string | null>(null);
  const imageViewportRef = useRef<HTMLDivElement | null>(null);
  const [geoZoom, setGeoZoom] = useState<number | null>(null);
  const saveGeoViewTimeoutRef = useRef<number | null>(null);
  const lastSavedGeoViewRef = useRef<MapViewRequest | null>(null);
  const lastAppliedGeoMapIdRef = useRef<string | null>(null);
  const geoDragStateRef = useRef<GeoDragState | null>(null);
  const lastPositionsMapIdRef = useRef<string | null>(null);
  const geoFovFeaturesRef = useRef<any[]>([]);
  const { ref: imageViewportSizeRef, width: viewportWidth, height: viewportHeight } =
    useElementSize();
  const [imageNaturalSize, setImageNaturalSize] = useState({ width: 0, height: 0 });
  const [imageView, setImageView] = useState({ scale: 1, translateX: 0, translateY: 0 });
  const dragStateRef = useRef<DragState | null>(null);
  const lastImageMapIdRef = useRef<string | null>(null);

  const mapsQuery = useQuery({
    queryKey: ['maps'],
    queryFn: listMaps
  });

  const mapOptionsQuery = useQuery({
    queryKey: ['maps', 'options'],
    queryFn: getMapOptions
  });

  useEffect(() => {
    selectedCameraIdRef.current = selectedCameraId;
  }, [selectedCameraId]);

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

  useEffect(() => {
    if (!mapDetailQuery.data) {
      return;
    }
    const mapId = mapDetailQuery.data.map.id;
    const isMapChanged = lastPositionsMapIdRef.current !== mapId;
    if (!isMapChanged && positionsDirty) {
      return;
    }
    setPositions(mapDetailQuery.data.cameras.map((camera) => toPositionRequest(camera)));
    setPositionsDirty(false);
    lastPositionsMapIdRef.current = mapId;
  }, [mapDetailQuery.data]);

  const activeMap = mapDetailQuery.data?.map;

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

    setGeoZoom(map.getZoom());
    lastSavedGeoViewRef.current = {
      geoCenterLatitude: map.getCenter().lat,
      geoCenterLongitude: map.getCenter().lng,
      geoZoom: map.getZoom()
    };
    lastAppliedGeoMapIdRef.current = mapId;
  }, [activeMap?.type, mapDetailQuery.data?.map.id]);

  const createMapMutation = useMutation({
    mutationFn: createMap,
    onSuccess: (map) => {
      queryClient.invalidateQueries({ queryKey: ['maps'] });
      setSelectedMapId(map.id);
      setNewMapName('');
      setNewMapType('Image');
      setNewMapParentId(null);
      createHandlers.close();
      notifications.show({
        title: t('pages.maps.notifications.create.title'),
        message: t('pages.maps.notifications.create.message'),
        color: 'brand'
      });
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.maps.notifications.createFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const updateMapMutation = useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: MapLayoutRequest }) =>
      updateMap(id, payload),
    onSuccess: (map) => {
      queryClient.invalidateQueries({ queryKey: ['maps'] });
      queryClient.invalidateQueries({ queryKey: ['maps', map.id] });
      editHandlers.close();
      notifications.show({
        title: t('pages.maps.notifications.update.title'),
        message: t('pages.maps.notifications.update.message', { name: map.name }),
        color: 'brand'
      });
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.maps.notifications.updateFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const deleteMapMutation = useMutation({
    mutationFn: (id: string) => deleteMap(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['maps'] });
      setSelectedMapId(null);
      setDeleteTarget(null);
      deleteHandlers.close();
      notifications.show({
        title: t('pages.maps.notifications.delete.title'),
        message: t('pages.maps.notifications.delete.message'),
        color: 'brand'
      });
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.maps.notifications.deleteFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const uploadImageMutation = useMutation({
    mutationFn: (file: File) => uploadMapImage(selectedMapId ?? '', file),
    onSuccess: (map) => {
      queryClient.invalidateQueries({ queryKey: ['maps', selectedMapId] });
      notifications.show({
        title: t('pages.maps.notifications.imageUploaded.title'),
        message: t('pages.maps.notifications.imageUploaded.message', { name: map.name }),
        color: 'brand'
      });
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.maps.notifications.uploadFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const savePositionsMutation = useMutation({
    mutationFn: (payload: MapCameraPositionRequest[]) =>
      saveMapCameras(selectedMapId ?? '', payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['maps', selectedMapId] });
      setPositionsDirty(false);
      notifications.show({
        title: t('pages.maps.notifications.positionsSaved.title'),
        message: t('pages.maps.notifications.positionsSaved.message'),
        color: 'brand'
      });
    },
    onError: (error: Error) => {
      notifications.show({
        title: t('pages.maps.notifications.positionsFailed.title'),
        message: error.message,
        color: 'red'
      });
    }
  });

  const saveGeoViewMutation = useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: MapViewRequest }) =>
      updateMapView(id, payload),
    onSuccess: (map) => {
      queryClient.invalidateQueries({ queryKey: ['maps'] });
      queryClient.setQueryData(['maps', map.id], (prev: any) => {
        if (!prev?.map) {
          return prev;
        }
        return { ...prev, map: { ...prev.map, ...map } };
      });
      lastSavedGeoViewRef.current = {
        geoCenterLatitude: map.geoCenterLatitude ?? 0,
        geoCenterLongitude: map.geoCenterLongitude ?? 0,
        geoZoom: map.geoZoom ?? 0
      };
    }
  });

  const geoStyleUrl = mapOptionsQuery.data?.geoStyleUrl?.trim();

  const filteredMaps = useMemo(() => {
    const term = mapSearch.trim().toLowerCase();
    if (!term) {
      return mapsQuery.data ?? [];
    }
    return (mapsQuery.data ?? []).filter((map) =>
      map.name.toLowerCase().includes(term)
    );
  }, [mapSearch, mapsQuery.data]);

  const mapTree = useMemo(() => buildMapTree(filteredMaps), [filteredMaps]);

  const parentOptions = useMemo(() => flattenMapTree(mapTree), [mapTree]);

  const editParentOptions = useMemo(
    () => flattenMapTree(mapTree, 0, editMap?.id),
    [mapTree, editMap]
  );

  const positionsByCamera = useMemo(() => {
    return new Map(positions.map((position) => [position.cameraId, position]));
  }, [positions]);

  const filteredCameras = useMemo(() => {
    const term = search.trim().toLowerCase();
    if (!term) {
      return cameras;
    }

    return cameras.filter((camera) => {
      return (
        (camera.code ?? '').toLowerCase().includes(term) ||
        camera.ipAddress.toLowerCase().includes(term)
      );
    });
  }, [cameras, search]);

  const cameraById = useMemo(() => {
    return new Map(cameras.map((camera) => [camera.cameraId, camera]));
  }, [cameras]);

  const resolveMapTypeLabel = (type?: MapLayoutType | null) => {
    if (type === 'Geo') {
      return t('pages.maps.types.geo');
    }
    if (type === 'Image') {
      return t('pages.maps.types.image');
    }
    return type ?? '-';
  };

  const resolveCameraLabel = (cameraId: string) => {
    const camera = cameraById.get(cameraId);
    if (!camera) {
      return cameraId;
    }
    const code = camera.code?.trim();
    const ip = camera.ipAddress?.trim();
    if (code && ip) {
      return `${code} - ${ip}`;
    }
    return code || ip || cameraId;
  };

  const createCameraMarkerElement = () => {
    const wrapper = document.createElement('div');
    wrapper.style.position = 'relative';
    wrapper.style.width = '0';
    wrapper.style.height = '0';
    wrapper.style.pointerEvents = 'none';

    const iconWrap = document.createElement('div');
    iconWrap.dataset.role = 'icon-wrap';
    iconWrap.style.position = 'absolute';
    iconWrap.style.left = '0';
    iconWrap.style.top = '0';
    iconWrap.style.transform = 'translate(-50%, -50%)';
    iconWrap.style.pointerEvents = 'auto';
    iconWrap.style.cursor = 'grab';

    const icon = document.createElement('div');
    icon.dataset.role = 'icon';
    icon.style.width = '100%';
    icon.style.height = '100%';
    icon.style.backgroundImage = 'url(/ipro-camera.svg)';
    icon.style.backgroundSize = 'contain';
    icon.style.backgroundRepeat = 'no-repeat';
    icon.style.filter = 'drop-shadow(0 2px 4px rgba(0, 0, 0, 0.45))';
    icon.style.transformOrigin = 'center center';

    const label = document.createElement('div');
    label.dataset.role = 'label';
    label.style.position = 'absolute';
    label.style.left = '0';
    label.style.top = '0';
    label.style.transform = 'translate(-50%, -100%)';
    label.style.padding = '2px 6px';
    label.style.borderRadius = '999px';
    label.style.fontSize = '12px';
    label.style.fontWeight = '600';
    label.style.background = '#ffffff';
    label.style.color = '#111111';
    label.style.whiteSpace = 'nowrap';
    label.style.pointerEvents = 'none';

    const removeBtn = document.createElement('button');
    removeBtn.dataset.role = 'remove-btn';
    removeBtn.style.position = 'absolute';
    removeBtn.style.left = '0';
    removeBtn.style.top = '0';
    removeBtn.style.width = '22px';
    removeBtn.style.height = '22px';
    removeBtn.style.borderRadius = '50%';
    removeBtn.style.border = 'none';
    removeBtn.style.background = '#e03131';
    removeBtn.style.color = 'white';
    removeBtn.style.fontSize = '12px';
    removeBtn.style.cursor = 'pointer';
    removeBtn.style.display = 'none';
    removeBtn.style.padding = '0';
    removeBtn.style.fontWeight = 'bold';
    removeBtn.style.boxShadow = '0 2px 6px rgba(0, 0, 0, 0.35)';
    removeBtn.textContent = 'x';
    removeBtn.style.pointerEvents = 'auto';

    const rotateHandle = document.createElement('div');
    rotateHandle.dataset.role = 'rotate-handle';
    rotateHandle.style.position = 'absolute';
    rotateHandle.style.left = '0';
    rotateHandle.style.top = '0';
    rotateHandle.style.width = `${HANDLE_SIZE}px`;
    rotateHandle.style.height = `${HANDLE_SIZE}px`;
    rotateHandle.style.borderRadius = '50%';
    rotateHandle.style.background = '#4cc9f0';
    rotateHandle.style.border = '2px solid #111';
    rotateHandle.style.cursor = 'grab';
    rotateHandle.style.display = 'none';
    rotateHandle.style.pointerEvents = 'auto';

    const createScaleHandle = (role: string) => {
      const handle = document.createElement('div');
      handle.dataset.role = 'scale-handle';
      handle.dataset.handle = role;
      handle.style.position = 'absolute';
      handle.style.left = '0';
      handle.style.top = '0';
      handle.style.width = `${HANDLE_SIZE}px`;
      handle.style.height = `${HANDLE_SIZE}px`;
      handle.style.background = '#f36f21';
      handle.style.border = '2px solid #111';
      handle.style.borderRadius = '4px';
      handle.style.cursor = 'nwse-resize';
      handle.style.display = 'none';
      handle.style.pointerEvents = 'auto';
      return handle;
    };

    const scaleHandles = [
      createScaleHandle('tl'),
      createScaleHandle('tr'),
      createScaleHandle('bl'),
      createScaleHandle('br')
    ];

    const fovLeft = document.createElement('div');
    fovLeft.dataset.role = 'fov-left';
    fovLeft.style.position = 'absolute';
    fovLeft.style.left = '0';
    fovLeft.style.top = '0';
    fovLeft.style.width = `${HANDLE_SIZE}px`;
    fovLeft.style.height = `${HANDLE_SIZE}px`;
    fovLeft.style.borderRadius = '50%';
    fovLeft.style.background = '#f36f21';
    fovLeft.style.border = '2px solid #111';
    fovLeft.style.transform = 'translate(-50%, -50%)';
    fovLeft.style.cursor = 'pointer';
    fovLeft.style.display = 'none';
    fovLeft.style.pointerEvents = 'auto';

    const fovRight = document.createElement('div');
    fovRight.dataset.role = 'fov-right';
    fovRight.style.position = 'absolute';
    fovRight.style.left = '0';
    fovRight.style.top = '0';
    fovRight.style.width = `${HANDLE_SIZE}px`;
    fovRight.style.height = `${HANDLE_SIZE}px`;
    fovRight.style.borderRadius = '50%';
    fovRight.style.background = '#f36f21';
    fovRight.style.border = '2px solid #111';
    fovRight.style.transform = 'translate(-50%, -50%)';
    fovRight.style.cursor = 'pointer';
    fovRight.style.display = 'none';
    fovRight.style.pointerEvents = 'auto';

    iconWrap.appendChild(icon);
    wrapper.appendChild(label);
    wrapper.appendChild(fovLeft);
    wrapper.appendChild(fovRight);
    wrapper.appendChild(removeBtn);
    wrapper.appendChild(rotateHandle);
    scaleHandles.forEach((handle) => wrapper.appendChild(handle));
    wrapper.appendChild(iconWrap);

    return wrapper;
  };

  const updateCameraMarkerElement = (
    element: HTMLElement,
    params: {
      label: string;
      angle: number;
      scale: number;
      selected: boolean;
      viewScale: number;
      latitude: number;
      longitude: number;
      fovDegrees: number;
      range: number;
      map: MapLibreMap;
      cameraId?: string;
    }
  ) => {
    const iconWrap = element.querySelector('[data-role="icon-wrap"]') as HTMLDivElement | null;
    const icon = element.querySelector('[data-role="icon"]') as HTMLDivElement | null;
    const label = element.querySelector('[data-role="label"]') as HTMLDivElement | null;
    const removeBtn = element.querySelector('[data-role="remove-btn"]') as HTMLButtonElement | null;
    const rotateHandle = element.querySelector('[data-role="rotate-handle"]') as HTMLDivElement | null;
    const fovLeft = element.querySelector('[data-role="fov-left"]') as HTMLDivElement | null;
    const fovRight = element.querySelector('[data-role="fov-right"]') as HTMLDivElement | null;
    const scaleHandles = Array.from(
      element.querySelectorAll('[data-role="scale-handle"]')
    ) as HTMLDivElement[];
    const viewScale = params.viewScale;
    const labelScale = clamp(viewScale, 0.7, 1.2);
    const handleSize = Math.round(HANDLE_SIZE * viewScale);
    const rotateOffset = Math.round(ROTATE_HANDLE_OFFSET * viewScale);
    const iconSize = Math.round(BASE_ICON_SIZE * viewScale * params.scale);
    const labelOffset = params.selected
      ? iconSize + rotateOffset + handleSize + Math.round(8 * viewScale)
      : iconSize + Math.round(6 * viewScale);

    if (iconWrap) {
      iconWrap.style.width = `${iconSize}px`;
      iconWrap.style.height = `${iconSize}px`;
      iconWrap.style.border = params.selected ? '1px dashed rgba(243, 111, 33, 0.7)' : 'none';
      iconWrap.style.borderRadius = '8px';
    }
    if (icon) {
      icon.style.transform = `rotate(${normalizeAngle(params.angle + ICON_ROTATION_OFFSET)}deg)`;
    }
    if (label) {
      label.textContent = params.label;
      label.style.fontSize = `${Math.round(11 * labelScale)}px`;
      label.style.padding = `${Math.round(2 * labelScale)}px ${Math.round(6 * labelScale)}px`;
      label.style.top = `${-labelOffset}px`;
      label.style.display = params.label ? 'block' : 'none';
    }
    if (removeBtn) {
      const btnSize = Math.round(22 * viewScale);
      const removeLeft = iconSize / 2 + rotateOffset - btnSize;
      const removeTop = -iconSize / 2 - rotateOffset - handleSize - Math.round(8 * viewScale);
      removeBtn.style.width = `${btnSize}px`;
      removeBtn.style.height = `${btnSize}px`;
      removeBtn.style.left = `${removeLeft}px`;
      removeBtn.style.top = `${removeTop}px`;
      removeBtn.style.fontSize = `${Math.max(10, Math.round(12 * viewScale))}px`;
      removeBtn.style.display = params.selected ? 'block' : 'none';
      if (params.selected && params.cameraId) {
        removeBtn.onclick = (e) => {
          e.stopPropagation();
          removePosition(params.cameraId!);
        };
      }
    }

    if (rotateHandle) {
      rotateHandle.style.width = `${handleSize}px`;
      rotateHandle.style.height = `${handleSize}px`;
      rotateHandle.style.left = `${-handleSize / 2}px`;
      rotateHandle.style.top = `${-iconSize / 2 - rotateOffset - handleSize}px`;
      rotateHandle.style.display = params.selected ? 'block' : 'none';
    }

    scaleHandles.forEach((handle) => {
      const key = handle.dataset.handle ?? 'tl';
      const isLeft = key === 'tl' || key === 'bl';
      const isTop = key === 'tl' || key === 'tr';
      const left =
        (isLeft ? -handleSize / 2 : iconSize - handleSize / 2) - iconSize / 2;
      const top =
        (isTop ? -handleSize / 2 : iconSize - handleSize / 2) - iconSize / 2;
      handle.style.width = `${handleSize}px`;
      handle.style.height = `${handleSize}px`;
      handle.style.left = `${left}px`;
      handle.style.top = `${top}px`;
      handle.style.display = params.selected ? 'block' : 'none';
    });

    if (fovLeft && fovRight) {
      if (!params.selected) {
        fovLeft.style.display = 'none';
        fovRight.style.display = 'none';
      } else {
        const safeRange = Math.max(1, params.range) * params.scale / params.viewScale;
        const left = destinationPoint(
          params.latitude,
          params.longitude,
          params.angle - params.fovDegrees / 2,
          safeRange
        );
        const right = destinationPoint(
          params.latitude,
          params.longitude,
          params.angle + params.fovDegrees / 2,
          safeRange
        );
        const centerPx = params.map.project([params.longitude, params.latitude]);
        const leftPx = params.map.project([left.longitude, left.latitude]);
        const rightPx = params.map.project([right.longitude, right.latitude]);
        const leftOffset = { x: leftPx.x - centerPx.x, y: leftPx.y - centerPx.y };
        const rightOffset = { x: rightPx.x - centerPx.x, y: rightPx.y - centerPx.y };
        fovLeft.style.width = `${handleSize}px`;
        fovLeft.style.height = `${handleSize}px`;
        fovRight.style.width = `${handleSize}px`;
        fovRight.style.height = `${handleSize}px`;
        fovLeft.style.left = `${leftOffset.x}px`;
        fovLeft.style.top = `${leftOffset.y}px`;
        fovRight.style.left = `${rightOffset.x}px`;
        fovRight.style.top = `${rightOffset.y}px`;
        fovLeft.style.display = 'block';
        fovRight.style.display = 'block';
      }
    }

    if (iconWrap && params.cameraId) {
      iconWrap.onpointerdown = (event) => {
        startGeoCameraDrag(event, params.cameraId!);
      };
    }

    if (rotateHandle && params.cameraId) {
      rotateHandle.onpointerdown = (event) => {
        startGeoRotateDrag(event, params.cameraId!);
      };
    }

    if (fovLeft && params.cameraId) {
      fovLeft.onpointerdown = (event) => {
        startGeoFovDrag(event, params.cameraId!, 'left');
      };
    }

    if (fovRight && params.cameraId) {
      fovRight.onpointerdown = (event) => {
        startGeoFovDrag(event, params.cameraId!, 'right');
      };
    }

    scaleHandles.forEach((handle) => {
      if (!params.cameraId) {
        return;
      }
      handle.onpointerdown = (event) => {
        startGeoScaleDrag(event, params.cameraId!);
      };
    });
  };


  const imageFovShapes = useMemo(() => {
    if (!activeMap || activeMap.type !== 'Image') {
      return [];
    }
    if (imageNaturalSize.width <= 0 || imageNaturalSize.height <= 0) {
      return [];
    }
    const scale = Math.min(imageNaturalSize.width, imageNaturalSize.height);
    return positions
      .filter((position) => position.x != null && position.y != null)
      .map((position) => {
        const rangeValue =
          typeof position.range === 'number' ? position.range : DEFAULT_RANGE_IMAGE;
        const safeRange = clamp01(rangeValue);
        const iconScale = position.iconScale ?? DEFAULT_ICON_SCALE;
        const angle =
          typeof position.angleDegrees === 'number'
            ? position.angleDegrees
            : DEFAULT_ANGLE_DEGREES;
        const fov =
          typeof position.fovDegrees === 'number'
            ? clampFov(position.fovDegrees)
            : DEFAULT_FOV_DEGREES;
        const px = (position.x ?? 0) * imageNaturalSize.width;
        const py = (position.y ?? 0) * imageNaturalSize.height;
        return {
          cameraId: position.cameraId,
          points: buildImageSectorPoints(
            px,
            py,
            angle,
            safeRange * scale * iconScale,
            fov
          )
        };
      });
  }, [activeMap, imageNaturalSize, positions]);

  const geoFovFeatures = useMemo(() => {
    if (!activeMap || activeMap.type !== 'Geo') {
      return [];
    }
    return positions
      .filter((position) => position.latitude != null && position.longitude != null)
      .map((position) => {
        const angle =
          typeof position.angleDegrees === 'number'
            ? position.angleDegrees
            : DEFAULT_ANGLE_DEGREES;
        const fov =
          typeof position.fovDegrees === 'number'
            ? clampFov(position.fovDegrees)
            : DEFAULT_FOV_DEGREES;
        const rangeValue =
          typeof position.range === 'number' ? position.range : DEFAULT_RANGE_GEO;
        const iconScale = position.iconScale ?? DEFAULT_ICON_SCALE;
        const zoom = geoZoom ?? activeMap?.geoZoom ?? 11;
        const viewScale = getGeoMarkerScale(zoom);
        const safeRange = Math.max(1, rangeValue) * iconScale / viewScale;
        const left = destinationPoint(
          position.latitude ?? 0,
          position.longitude ?? 0,
          angle - fov / 2,
          safeRange
        );
        const right = destinationPoint(
          position.latitude ?? 0,
          position.longitude ?? 0,
          angle + fov / 2,
          safeRange
        );
        return {
          type: 'Feature',
          properties: { cameraId: position.cameraId },
          geometry: {
            type: 'Polygon',
            coordinates: [
              [
                [position.longitude ?? 0, position.latitude ?? 0],
                [left.longitude, left.latitude],
                [right.longitude, right.latitude],
                [position.longitude ?? 0, position.latitude ?? 0]
              ]
            ]
          }
        };
      });
  }, [activeMap, geoZoom, positions]);

  useEffect(() => {
    geoFovFeaturesRef.current = geoFovFeatures as any[];
  }, [geoFovFeatures]);

  const setImageViewportNode = (node: HTMLDivElement | null) => {
    imageViewportRef.current = node;
    const _refAny: any = imageViewportSizeRef;
    if (typeof _refAny === 'function') {
      _refAny(node);
    } else if (_refAny) {
      _refAny.current = node;
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

    const shouldReset = lastImageMapIdRef.current !== activeMap.id;
    if (shouldReset || isFullscreen) {
      resetImageView();
    }
    lastImageMapIdRef.current = activeMap.id;
  }, [
    activeMap,
    imageNaturalSize.height,
    imageNaturalSize.width,
    viewportHeight,
    viewportWidth,
    isFullscreen
  ]);

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
      setGeoZoom(map.getZoom());
      lastSavedGeoViewRef.current = {
        geoCenterLatitude: map.getCenter().lat,
        geoCenterLongitude: map.getCenter().lng,
        geoZoom: map.getZoom()
      };
      if (map.getSource('camerfov')) {
        return;
      }
      map.addSource('camerfov', {
        type: 'geojson',
        data: {
          type: 'FeatureCollection',
          features: []
        }
      });
      map.addLayer({
        id: 'camerfov-fill',
        type: 'fill',
        source: 'camerfov',
        paint: {
          'fill-color': '#4cc9f0',
          'fill-opacity': 0.25
        }
      });
      map.addLayer({
        id: 'camerfov-outline',
        type: 'line',
        source: 'camerfov',
        paint: {
          'line-color': '#4cc9f0',
          'line-width': 1.5
        }
      });
      const source = map.getSource('camerfov') as maplibregl.GeoJSONSource | undefined;
      if (source) {
        source.setData({
          type: 'FeatureCollection',
          features: geoFovFeaturesRef.current as any
        });
      }
    });
    map.on('click', (event) => {
      const target = event.originalEvent?.target as HTMLElement | null;
      if (
        target &&
        target.closest(
          '[data-role="icon-wrap"], [data-role="label"], [data-role="remove-btn"], [data-role="rotate-handle"], [data-role="scale-handle"], [data-role="fov-left"], [data-role="fov-right"]'
        )
      ) {
        return;
      }
      if (selectedCameraIdRef.current) {
        setSelectedCameraId(null);
      }
    });

    mapInstanceRef.current = map;
  }, [activeMap?.type, geoStyleUrl, positions]);

  useEffect(() => {
    const map = mapInstanceRef.current;
    if (!map || activeMap?.type !== 'Geo') {
      return;
    }
    map.resize();
  }, [activeMap?.type, isFullscreen]);

  useEffect(() => {
    const map = mapInstanceRef.current;
    if (!map || activeMap?.type !== 'Geo') {
      return;
    }

    const handleZoom = () => {
      setGeoZoom(map.getZoom());
    };

    handleZoom();
    map.on('zoom', handleZoom);
    map.on('zoomend', handleZoom);

    return () => {
      map.off('zoom', handleZoom);
      map.off('zoomend', handleZoom);
    };
  }, [activeMap?.type]);

  useEffect(() => {
    const map = mapInstanceRef.current;
    if (!map || activeMap?.type !== 'Geo' || !activeMap?.id) {
      return;
    }

    const handleMoveEnd = () => {
      const center = map.getCenter();
      const payload: MapViewRequest = {
        geoCenterLatitude: center.lat,
        geoCenterLongitude: center.lng,
        geoZoom: map.getZoom()
      };

      const last = lastSavedGeoViewRef.current;
      if (
        last &&
        Math.abs(last.geoCenterLatitude - payload.geoCenterLatitude) < 1e-6 &&
        Math.abs(last.geoCenterLongitude - payload.geoCenterLongitude) < 1e-6 &&
        Math.abs(last.geoZoom - payload.geoZoom) < 1e-3
      ) {
        return;
      }

      if (saveGeoViewTimeoutRef.current) {
        window.clearTimeout(saveGeoViewTimeoutRef.current);
      }

      saveGeoViewTimeoutRef.current = window.setTimeout(() => {
        saveGeoViewMutation.mutate({ id: activeMap.id, payload });
        lastSavedGeoViewRef.current = payload;
      }, 500);
    };

    map.on('moveend', handleMoveEnd);

    return () => {
      map.off('moveend', handleMoveEnd);
      if (saveGeoViewTimeoutRef.current) {
        window.clearTimeout(saveGeoViewTimeoutRef.current);
      }
    };
  }, [activeMap?.id, activeMap?.type, saveGeoViewMutation]);

  useEffect(() => {
    const map = mapInstanceRef.current;
    if (!map || activeMap?.type !== 'Geo') {
      return;
    }

    const zoom = geoZoom ?? map.getZoom();
    const viewScale = getGeoMarkerScale(zoom);
    const markerMap = mapMarkersRef.current;
    const nextIds = new Set<string>();
    positions.forEach((position) => {
      if (position.latitude == null || position.longitude == null) {
        return;
      }
      nextIds.add(position.cameraId);
      const existing = markerMap.get(position.cameraId);
      if (existing) {
        existing.setLngLat([position.longitude, position.latitude]);
        const label = resolveCameraLabel(position.cameraId);
        updateCameraMarkerElement(existing.getElement(), {
          label,
          angle: position.angleDegrees ?? DEFAULT_ANGLE_DEGREES,
          scale: position.iconScale ?? DEFAULT_ICON_SCALE,
          selected: selectedCameraId === position.cameraId,
          viewScale,
          latitude: position.latitude,
          longitude: position.longitude,
          fovDegrees:
            typeof position.fovDegrees === 'number'
              ? clampFov(position.fovDegrees)
              : DEFAULT_FOV_DEGREES,
          range: typeof position.range === 'number' ? position.range : DEFAULT_RANGE_GEO,
          map,
          cameraId: position.cameraId
        });
        return;
      }
      const element = createCameraMarkerElement();
      const label = resolveCameraLabel(position.cameraId);
      updateCameraMarkerElement(element, {
        label,
        angle: position.angleDegrees ?? DEFAULT_ANGLE_DEGREES,
        scale: position.iconScale ?? DEFAULT_ICON_SCALE,
        selected: selectedCameraId === position.cameraId,
        viewScale,
        latitude: position.latitude,
        longitude: position.longitude,
        fovDegrees:
          typeof position.fovDegrees === 'number'
            ? clampFov(position.fovDegrees)
            : DEFAULT_FOV_DEGREES,
        range: typeof position.range === 'number' ? position.range : DEFAULT_RANGE_GEO,
        map,
        cameraId: position.cameraId
      });
      const marker = new maplibregl.Marker({ element, draggable: false })
        .setLngLat([position.longitude, position.latitude])
        .addTo(map);
      markerMap.set(position.cameraId, marker);
    });

    for (const [cameraId, marker] of markerMap.entries()) {
      if (!nextIds.has(cameraId)) {
        marker.remove();
        markerMap.delete(cameraId);
      }
    }
  }, [activeMap?.type, geoZoom, positions, selectedCameraId]);

  useEffect(() => {
    const map = mapInstanceRef.current;
    if (!map || activeMap?.type !== 'Geo') {
      return;
    }
    const source = map.getSource('camerfov') as maplibregl.GeoJSONSource | undefined;
    if (!source) {
      return;
    }
    source.setData({
      type: 'FeatureCollection',
      features: geoFovFeatures as any
    });
  }, [activeMap?.type, geoFovFeatures]);

  useEffect(() => {
    const handlePointerMove = (event: PointerEvent) => {
      const dragState = dragStateRef.current;
      if (!dragState) {
        return;
      }
      if (activeMap?.type !== 'Image') {
        return;
      }
      const container = imageViewportRef.current;
      if (!container) {
        return;
      }
      const rect = container.getBoundingClientRect();
      const screenX = event.clientX - rect.left;
      const screenY = event.clientY - rect.top;
      const mapX = (screenX - imageView.translateX) / imageView.scale;
      const mapY = (screenY - imageView.translateY) / imageView.scale;

      if (dragState.kind === 'pan') {
        setImageView((prev) => ({
          ...prev,
          translateX: dragState.originX + (event.clientX - dragState.startX),
          translateY: dragState.originY + (event.clientY - dragState.startY)
        }));
        return;
      }

      if (imageNaturalSize.width === 0 || imageNaturalSize.height === 0) {
        return;
      }

      if (dragState.kind === 'camera') {
        const centerX = mapX - dragState.offsetX;
        const centerY = mapY - dragState.offsetY;
        updatePosition(dragState.cameraId, {
          x: clamp01(centerX / imageNaturalSize.width),
          y: clamp01(centerY / imageNaturalSize.height)
        });
        return;
      }

      const position = positionsByCamera.get(dragState.cameraId);
      if (!position || position.x == null || position.y == null) {
        return;
      }

      const centerX = position.x * imageNaturalSize.width;
      const centerY = position.y * imageNaturalSize.height;
      const dx = mapX - centerX;
      const dy = mapY - centerY;
      const distance = Math.max(1, Math.hypot(dx, dy));
      const bearing = getBearingDegrees(dx, dy);

      if (dragState.kind === 'fov') {
        const iconScale = Math.max(0.1, position.iconScale ?? DEFAULT_ICON_SCALE);
        const baseRange = clamp01(
          distance / (Math.min(imageNaturalSize.width, imageNaturalSize.height) * iconScale)
        );
        const currentAngle =
          typeof position.angleDegrees === 'number'
            ? position.angleDegrees
            : DEFAULT_ANGLE_DEGREES;
        const currentFov =
          typeof position.fovDegrees === 'number'
            ? clampFov(position.fovDegrees)
            : DEFAULT_FOV_DEGREES;
        const leftEdge = normalizeAngle(currentAngle - currentFov / 2);
        const rightEdge = normalizeAngle(currentAngle + currentFov / 2);
        const nextLeft = dragState.side === 'left' ? bearing : leftEdge;
        const nextRight = dragState.side === 'right' ? bearing : rightEdge;
        const nextFov = clampFov(angleDistance(nextLeft, nextRight));
        const nextAngle = meanAngle(nextLeft, nextRight);
        updatePosition(dragState.cameraId, {
          angleDegrees: nextAngle,
          range: baseRange,
          fovDegrees: nextFov
        });
        return;
      }

      if (dragState.kind === 'rotate') {
        updatePosition(dragState.cameraId, {
          angleDegrees: normalizeAngle(bearing)
        });
        return;
      }

      if (dragState.kind === 'scale') {
        const nextScale = clamp(
          dragState.startScale * (distance / dragState.startDistance),
          MIN_ICON_SCALE,
          MAX_ICON_SCALE
        );
        updatePosition(dragState.cameraId, { iconScale: nextScale });
      }
    };

    const handlePointerUp = () => {
      dragStateRef.current = null;
    };

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', handlePointerUp);

    return () => {
      window.removeEventListener('pointermove', handlePointerMove);
      window.removeEventListener('pointerup', handlePointerUp);
    };
  }, [
    activeMap?.type,
    imageNaturalSize.height,
    imageNaturalSize.width,
    imageView.scale,
    imageView.translateX,
    imageView.translateY,
    positionsByCamera
  ]);

  useEffect(() => {
    if (activeMap?.type !== 'Geo') {
      return;
    }

    const handlePointerMove = (event: PointerEvent) => {
      const dragState = geoDragStateRef.current;
      if (!dragState) {
        return;
      }
      const map = mapInstanceRef.current;
      const pointer = getGeoPointerPoint(event);
      if (!map || !pointer) {
        return;
      }
      const position = positionsByCamera.get(dragState.cameraId);
      if (position?.latitude == null || position?.longitude == null) {
        return;
      }

      if (dragState.kind === 'camera') {
        const target = {
          x: pointer.x - dragState.offsetX,
          y: pointer.y - dragState.offsetY
        };
        const lngLat = map.unproject([target.x, target.y]);
        updatePosition(dragState.cameraId, {
          latitude: lngLat.lat,
          longitude: lngLat.lng
        });
        return;
      }

      const center = { lat: position.latitude, lng: position.longitude };
      const targetLngLat = map.unproject([pointer.x, pointer.y]);
      const bearing = getGeoBearing(
        center.lat,
        center.lng,
        targetLngLat.lat,
        targetLngLat.lng
      );
      const distance = getGeoDistance(
        center.lat,
        center.lng,
        targetLngLat.lat,
        targetLngLat.lng
      );

      if (dragState.kind === 'fov') {
        const iconScale = Math.max(0.1, position.iconScale ?? DEFAULT_ICON_SCALE);
        const zoom = geoZoom ?? map.getZoom();
        const viewScale = getGeoMarkerScale(zoom);
        const baseRange = Math.max(1, (distance * viewScale) / iconScale);
        const currentAngle =
          typeof position.angleDegrees === 'number'
            ? position.angleDegrees
            : DEFAULT_ANGLE_DEGREES;
        const currentFov =
          typeof position.fovDegrees === 'number'
            ? clampFov(position.fovDegrees)
            : DEFAULT_FOV_DEGREES;
        const leftEdge = normalizeAngle(currentAngle - currentFov / 2);
        const rightEdge = normalizeAngle(currentAngle + currentFov / 2);
        const nextLeft = dragState.side === 'left' ? bearing : leftEdge;
        const nextRight = dragState.side === 'right' ? bearing : rightEdge;
        const nextFov = clampFov(angleDistance(nextLeft, nextRight));
        const nextAngle = meanAngle(nextLeft, nextRight);
        updatePosition(dragState.cameraId, {
          angleDegrees: nextAngle,
          range: baseRange,
          fovDegrees: nextFov
        });
        return;
      }

      if (dragState.kind === 'rotate') {
        updatePosition(dragState.cameraId, {
          angleDegrees: normalizeAngle(bearing)
        });
        return;
      }

      if (dragState.kind === 'scale') {
        const centerPx = map.project([center.lng, center.lat]);
        const distancePx = Math.max(
          1,
          Math.hypot(pointer.x - centerPx.x, pointer.y - centerPx.y)
        );
        const nextScale = clamp(
          dragState.startScale * (distancePx / dragState.startDistance),
          MIN_ICON_SCALE,
          MAX_ICON_SCALE
        );
        updatePosition(dragState.cameraId, { iconScale: nextScale });
      }
    };

    const handlePointerUp = () => {
      if (!geoDragStateRef.current) {
        return;
      }
      geoDragStateRef.current = null;
      const map = mapInstanceRef.current;
      if (map) {
        map.dragPan.enable();
      }
    };

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', handlePointerUp);

    return () => {
      window.removeEventListener('pointermove', handlePointerMove);
      window.removeEventListener('pointerup', handlePointerUp);
    };
  }, [activeMap?.type, positionsByCamera]);

  const updatePosition = (cameraId: string, update: Partial<MapCameraPositionRequest>) => {
    setPositions((prev) => {
      const next = prev.filter((position) => position.cameraId !== cameraId);
      const existing = prev.find((position) => position.cameraId === cameraId);
      const defaultRange = activeMap?.type === 'Geo' ? DEFAULT_RANGE_GEO : DEFAULT_RANGE_IMAGE;
      next.push({
        cameraId,
        label: existing?.label,
        x: update.x ?? existing?.x,
        y: update.y ?? existing?.y,
        angleDegrees:
          update.angleDegrees ?? existing?.angleDegrees ?? DEFAULT_ANGLE_DEGREES,
        fovDegrees: update.fovDegrees ?? existing?.fovDegrees ?? DEFAULT_FOV_DEGREES,
        range: update.range ?? existing?.range ?? defaultRange,
        iconScale: update.iconScale ?? existing?.iconScale ?? DEFAULT_ICON_SCALE,
        latitude: update.latitude ?? existing?.latitude,
        longitude: update.longitude ?? existing?.longitude
      });
      return next;
    });
    setPositionsDirty(true);
  };

  const removePosition = (cameraId: string) => {
    setPositions((prev) => prev.filter((position) => position.cameraId !== cameraId));
    setPositionsDirty(true);
  };

  const handleImageDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    const cameraId = event.dataTransfer.getData('camera-id');
    if (!cameraId || !activeMap) {
      return;
    }
    const container = imageViewportRef.current;
    if (!container || imageNaturalSize.width === 0 || imageNaturalSize.height === 0) {
      return;
    }
    const rect = container.getBoundingClientRect();
    const screenX = event.clientX - rect.left;
    const screenY = event.clientY - rect.top;
    const mapX = (screenX - imageView.translateX) / imageView.scale;
    const mapY = (screenY - imageView.translateY) / imageView.scale;
    updatePosition(cameraId, {
      x: clamp01(mapX / imageNaturalSize.width),
      y: clamp01(mapY / imageNaturalSize.height)
    });
  };

  const handleGeoDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    const cameraId = event.dataTransfer.getData('camera-id');
    if (!cameraId) {
      return;
    }
    const map = mapInstanceRef.current;
    if (!map) {
      return;
    }
    const rect = event.currentTarget.getBoundingClientRect();
    const point = map.unproject([event.clientX - rect.left, event.clientY - rect.top]);
    updatePosition(cameraId, { latitude: point.lat, longitude: point.lng });
  };

  const getGeoPointerPoint = (event: PointerEvent) => {
    const container = mapContainerRef.current;
    if (!container) {
      return null;
    }
    const rect = container.getBoundingClientRect();
    return {
      x: event.clientX - rect.left,
      y: event.clientY - rect.top
    };
  };

  const startGeoCameraDrag = (event: PointerEvent, cameraId: string) => {
    if (event.button !== 0) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    setSelectedCameraId(cameraId);
    const map = mapInstanceRef.current;
    const pointer = getGeoPointerPoint(event);
    if (!map || !pointer) {
      return;
    }
    const position = positionsByCamera.get(cameraId);
    if (position?.latitude == null || position?.longitude == null) {
      return;
    }
    const center = map.project([position.longitude, position.latitude]);
    geoDragStateRef.current = {
      kind: 'camera',
      cameraId,
      offsetX: pointer.x - center.x,
      offsetY: pointer.y - center.y
    };
    map.dragPan.disable();
  };

  const startGeoFovDrag = (
    event: PointerEvent,
    cameraId: string,
    side: 'left' | 'right'
  ) => {
    event.preventDefault();
    event.stopPropagation();
    setSelectedCameraId(cameraId);
    const map = mapInstanceRef.current;
    if (!map) {
      return;
    }
    geoDragStateRef.current = { kind: 'fov', cameraId, side };
    map.dragPan.disable();
  };

  const startGeoRotateDrag = (event: PointerEvent, cameraId: string) => {
    event.preventDefault();
    event.stopPropagation();
    setSelectedCameraId(cameraId);
    const map = mapInstanceRef.current;
    if (!map) {
      return;
    }
    geoDragStateRef.current = { kind: 'rotate', cameraId };
    map.dragPan.disable();
  };

  const startGeoScaleDrag = (event: PointerEvent, cameraId: string) => {
    event.preventDefault();
    event.stopPropagation();
    setSelectedCameraId(cameraId);
    const map = mapInstanceRef.current;
    const pointer = getGeoPointerPoint(event);
    if (!map || !pointer) {
      return;
    }
    const position = positionsByCamera.get(cameraId);
    if (position?.latitude == null || position?.longitude == null) {
      return;
    }
    const center = map.project([position.longitude, position.latitude]);
    const distance = Math.max(1, Math.hypot(pointer.x - center.x, pointer.y - center.y));
    geoDragStateRef.current = {
      kind: 'scale',
      cameraId,
      startScale: position.iconScale ?? DEFAULT_ICON_SCALE,
      startDistance: distance
    };
    map.dragPan.disable();
  };

  const handleImagePointerDown = (event: React.PointerEvent<HTMLDivElement>) => {
    if (event.button !== 0) {
      return;
    }
    const target = event.target as HTMLElement;
    if (target.closest('[data-no-pan="true"]')) {
      return;
    }
    if (selectedCameraIdRef.current) {
      setSelectedCameraId(null);
    }
    dragStateRef.current = {
      kind: 'pan',
      startX: event.clientX,
      startY: event.clientY,
      originX: imageView.translateX,
      originY: imageView.translateY
    };
  };

  const startCameraDrag = (event: React.PointerEvent<HTMLDivElement>, cameraId: string) => {
    if (event.button !== 0) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    setSelectedCameraId(cameraId);
    if (imageNaturalSize.width === 0 || imageNaturalSize.height === 0) {
      return;
    }
    const container = imageViewportRef.current;
    if (!container) {
      return;
    }
    const rect = container.getBoundingClientRect();
    const screenX = event.clientX - rect.left;
    const screenY = event.clientY - rect.top;
    const mapX = (screenX - imageView.translateX) / imageView.scale;
    const mapY = (screenY - imageView.translateY) / imageView.scale;
    const position = positionsByCamera.get(cameraId);
    const centerX = (position?.x ?? 0) * imageNaturalSize.width;
    const centerY = (position?.y ?? 0) * imageNaturalSize.height;
    dragStateRef.current = {
      kind: 'camera',
      cameraId,
      offsetX: mapX - centerX,
      offsetY: mapY - centerY
    };
  };

  const startFovDrag = (
    event: React.PointerEvent<HTMLDivElement>,
    cameraId: string,
    side: 'left' | 'right'
  ) => {
    event.preventDefault();
    event.stopPropagation();
    setSelectedCameraId(cameraId);
    dragStateRef.current = { kind: 'fov', cameraId, side };
  };

  const startRotateDrag = (event: React.PointerEvent<HTMLDivElement>, cameraId: string) => {
    event.preventDefault();
    event.stopPropagation();
    setSelectedCameraId(cameraId);
    dragStateRef.current = { kind: 'rotate', cameraId };
  };

  const startScaleDrag = (event: React.PointerEvent<HTMLDivElement>, cameraId: string) => {
    event.preventDefault();
    event.stopPropagation();
    setSelectedCameraId(cameraId);
    const position = positionsByCamera.get(cameraId);
    if (!position || position.x == null || position.y == null) {
      return;
    }
    if (!imageNaturalSize.width || !imageNaturalSize.height) {
      return;
    }
    const container = imageViewportRef.current;
    if (!container) {
      return;
    }
    const rect = container.getBoundingClientRect();
    const screenX = event.clientX - rect.left;
    const screenY = event.clientY - rect.top;
    const mapX = (screenX - imageView.translateX) / imageView.scale;
    const mapY = (screenY - imageView.translateY) / imageView.scale;
    const centerX = position.x * imageNaturalSize.width;
    const centerY = position.y * imageNaturalSize.height;
    const distance = Math.max(1, Math.hypot(mapX - centerX, mapY - centerY));
    dragStateRef.current = {
      kind: 'scale',
      cameraId,
      startScale: position.iconScale ?? DEFAULT_ICON_SCALE,
      startDistance: distance
    };
  };

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

  const resolvedImageUrl = activeMap?.imageUrl ? buildApiUrl(activeMap.imageUrl) : null;
  const mapHasImage = Boolean(resolvedImageUrl);

  const renderMapTree = (nodes: MapTreeNode[], depth = 0) => {
    return nodes.map((node) => {
      const isSelected = node.map.id === selectedMapId;
      return (
        <Box key={node.map.id}>
          <Paper
            p="xs"
            radius="md"
            withBorder
            onClick={() => setSelectedMapId(node.map.id)}
            style={{
              marginLeft: depth * 12,
              cursor: 'pointer',
              borderColor: isSelected ? 'var(--app-brand)' : undefined
            }}
          >
            <Group justify="space-between" align="flex-start" gap="xs" wrap="nowrap">
              <Stack gap={2} style={{ flex: 1, minWidth: 0 }}>
                <Text size="sm" fw={600} truncate>
                  {node.map.name}
                </Text>
                <Text size="xs" className="muted-text">
                  {node.map.type}
                </Text>
              </Stack>
              <Group gap="xs" wrap="nowrap">
                <ActionIcon
                  variant="subtle"
                  onClick={(event) => {
                    event.stopPropagation();
                    setEditMap(node.map);
                    setEditMapName(node.map.name);
                    setEditMapType(node.map.type);
                    setEditMapParentId(node.map.parentId ?? null);
                    editHandlers.open();
                  }}
                  aria-label="Edit map"
                >
                  <IconEdit size={16} />
                </ActionIcon>
                <ActionIcon
                  variant="subtle"
                  color="red"
                  onClick={(event) => {
                    event.stopPropagation();
                    setDeleteTarget(node.map);
                    deleteHandlers.open();
                  }}
                  aria-label="Delete map"
                >
                  <IconTrash size={16} />
                </ActionIcon>
              </Group>
            </Group>
          </Paper>
          {node.children.length > 0 && renderMapTree(node.children, depth + 1)}
        </Box>
      );
    });
  };

  const canSave = Boolean(selectedMapId) && positionsDirty && !savePositionsMutation.isPending;

  const renderEmptyMap = (message: string) => (
    <Box
      h="100%"
      style={{
        minHeight: 360,
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

  const renderImageMap = () => {
    const containerStyles: CSSProperties = {
      position: 'relative',
      borderRadius: 16,
      border: '1px solid var(--app-border)',
      background: mapHasImage
        ? 'linear-gradient(135deg, rgba(24,24,24,0.95), rgba(24,24,24,0.65))'
        : 'linear-gradient(135deg, rgba(243,111,33,0.12), rgba(90,90,90,0.08))',
      overflow: 'hidden',
      height: isFullscreen ? 'calc(100vh - 160px)' : '100%',
      minHeight: 360,
      width: '100%'
    };

    if (isFullscreen) {
      containerStyles.position = 'fixed';
      containerStyles.inset = '80px 64px';
      containerStyles.zIndex = 200;
      containerStyles.boxShadow = '0 16px 50px rgba(0,0,0,0.45)';
    }

    return (
      <>
        {isFullscreen && (
          <Box
            onClick={() => setIsFullscreen(false)}
            style={{
              position: 'fixed',
              inset: 0,
              background: 'rgba(5, 5, 5, 0.6)',
              zIndex: 199
            }}
          />
        )}
        <Box
          ref={setImageViewportNode}
          onDragOver={(event) => event.preventDefault()}
          onDrop={handleImageDrop}
          onPointerDown={handleImagePointerDown}
          onWheel={handleImageWheel}
          style={containerStyles}
        >
          {!mapHasImage && (
            <Group h="100%" align="center" justify="center">
              <Stack gap={4} align="center">
                <IconMapPin size={32} />
                <Text size="sm" className="muted-text">
                  Upload a floorplan image to start placing cameras.
                </Text>
              </Stack>
            </Group>
          )}
          {mapHasImage && (
            <>
              <Box
                style={{
                  position: 'absolute',
                  inset: 0,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  pointerEvents: 'none'
                }}
              >
                <Text size="xs" className="muted-text">
                  Use the mouse wheel to zoom and drag to pan.
                </Text>
              </Box>
              <Box
                style={{
                  position: 'absolute',
                  inset: 0,
                  pointerEvents: 'none'
                }}
              >
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
                    alt={activeMap?.name ?? t('pages.maps.panel.mapAlt')}
                    onLoad={(event) => {
                      const image = event.currentTarget;
                      if (image.naturalWidth && image.naturalHeight) {
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
                      inset: 0
                    }}
                  >
                    {imageFovShapes.map((shape) => (
                      <polygon
                        key={shape.cameraId}
                        points={shape.points}
                        fill="rgba(76, 201, 240, 0.2)"
                        stroke="rgba(76, 201, 240, 0.7)"
                        strokeWidth="1"
                      />
                    ))}
                  </svg>
                </Box>
              </Box>
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
                {positions.map((position) => {
                  if (position.x == null || position.y == null) {
                    return null;
                  }
                  const left = (position.x ?? 0) * imageNaturalSize.width;
                  const top = (position.y ?? 0) * imageNaturalSize.height;
                  const angle =
                    typeof position.angleDegrees === 'number'
                      ? position.angleDegrees
                      : DEFAULT_ANGLE_DEGREES;
                  const scale = position.iconScale ?? DEFAULT_ICON_SCALE;
                  const fov =
                    typeof position.fovDegrees === 'number'
                      ? clampFov(position.fovDegrees)
                      : DEFAULT_FOV_DEGREES;
                  const iconSize = BASE_ICON_SIZE * scale;
                  const label = resolveCameraLabel(position.cameraId);
                  const isSelected = selectedCameraId === position.cameraId;
                  const rangeValue =
                    typeof position.range === 'number' ? position.range : DEFAULT_RANGE_IMAGE;
                  const rangePixels =
                    clamp01(rangeValue) *
                    Math.min(imageNaturalSize.width, imageNaturalSize.height) *
                    scale;
                  const edges = getImageSectorEdges(
                    left,
                    top,
                    angle,
                    rangePixels,
                    fov
                  );
                  const labelOffset = isSelected
                    ? iconSize + ROTATE_HANDLE_OFFSET + HANDLE_SIZE + 8
                    : iconSize + 6;

                  return (
                    <Box key={position.cameraId} style={{ position: 'relative' }}>
                      {isSelected && (
                        <>
                          <Box
                            data-no-pan="true"
                            onPointerDown={(event) =>
                              startFovDrag(event, position.cameraId, 'left')
                            }
                            style={{
                              position: 'absolute',
                              left: edges.left.x,
                              top: edges.left.y,
                              width: HANDLE_SIZE,
                              height: HANDLE_SIZE,
                              borderRadius: '50%',
                              background: '#f36f21',
                              border: '2px solid #111',
                              transform: 'translate(-50%, -50%)',
                              cursor: 'pointer'
                            }}
                          />
                          <Box
                            data-no-pan="true"
                            onPointerDown={(event) =>
                              startFovDrag(event, position.cameraId, 'right')
                            }
                            style={{
                              position: 'absolute',
                              left: edges.right.x,
                              top: edges.right.y,
                              width: HANDLE_SIZE,
                              height: HANDLE_SIZE,
                              borderRadius: '50%',
                              background: '#f36f21',
                              border: '2px solid #111',
                              transform: 'translate(-50%, -50%)',
                            cursor: 'pointer'
                            }}
                          />
                        </>
                      )}
                      <Box
                        data-no-pan="true"
                        onPointerDown={(event) => startCameraDrag(event, position.cameraId)}
                        style={{
                          position: 'absolute',
                          left,
                          top,
                          transform: 'translate(-50%, -50%)',
                          cursor: 'grab'
                        }}
                      >
                        {isSelected && (
                          <ActionIcon
                            data-no-pan="true"
                            size="sm"
                            color="red"
                            variant="filled"
                            style={{
                              position: 'absolute',
                              top: -ROTATE_HANDLE_OFFSET - HANDLE_SIZE - 8,
                              right: -ROTATE_HANDLE_OFFSET,
                              zIndex: 2
                            }}
                            onClick={(event) => {
                              event.preventDefault();
                              event.stopPropagation();
                              removePosition(position.cameraId);
                            }}
                          >
                            <IconX size={12} />
                          </ActionIcon>
                        )}
                        <Box
                          style={{
                            position: 'relative',
                            width: iconSize,
                            height: iconSize
                          }}
                        >
                          <Box
                            style={{
                              width: iconSize,
                              height: iconSize,
                              background: 'url(/ipro-camera.svg) center / contain no-repeat',
                              filter: 'drop-shadow(0 2px 4px rgba(0, 0, 0, 0.45))',
                              transform: `rotate(${normalizeAngle(angle + ICON_ROTATION_OFFSET)}deg)`,
                              transformOrigin: 'center'
                            }}
                          />
                          {isSelected && (
                            <>
                              <Box
                                style={{
                                  position: 'absolute',
                                  inset: 0,
                                  border: '1px dashed rgba(243, 111, 33, 0.7)',
                                  borderRadius: 8
                                }}
                              />
                              <Box
                                data-no-pan="true"
                                onPointerDown={(event) => startRotateDrag(event, position.cameraId)}
                                style={{
                                  position: 'absolute',
                                  top: -ROTATE_HANDLE_OFFSET - HANDLE_SIZE,
                                  left: iconSize / 2 - HANDLE_SIZE / 2,
                                  width: HANDLE_SIZE,
                                  height: HANDLE_SIZE,
                                  borderRadius: '50%',
                                  background: '#4cc9f0',
                                  border: '2px solid #111',
                                  cursor: 'grab'
                                }}
                              />
                              {[0, 1, 2, 3].map((handle) => {
                                const isLeft = handle === 0 || handle === 2;
                                const isTop = handle === 0 || handle === 1;
                                return (
                                  <Box
                                    key={handle}
                                    data-no-pan="true"
                                    onPointerDown={(event) =>
                                      startScaleDrag(event, position.cameraId)
                                    }
                                    style={{
                                      position: 'absolute',
                                      width: HANDLE_SIZE,
                                      height: HANDLE_SIZE,
                                      background: '#f36f21',
                                      border: '2px solid #111',
                                      borderRadius: 4,
                                      left: isLeft ? -HANDLE_SIZE / 2 : iconSize - HANDLE_SIZE / 2,
                                      top: isTop ? -HANDLE_SIZE / 2 : iconSize - HANDLE_SIZE / 2,
                                      cursor: 'nwse-resize'
                                    }}
                                  />
                                );
                              })}
                            </>
                          )}
                        </Box>
                        {label && (
                          <Box
                            style={{
                              position: 'absolute',
                              bottom: labelOffset,
                              left: '50%',
                              transform: 'translateX(-50%)',
                              padding: '2px 6px',
                              borderRadius: 999,
                              background: '#ffffff',
                              color: '#111111',
                              fontSize: 11,
                              fontWeight: 600,
                              whiteSpace: 'nowrap'
                            }}
                          >
                            {label}
                          </Box>
                        )}
                      </Box>
                    </Box>
                  );
                })}
              </Box>
              <Box
                style={{
                  position: 'absolute',
                  top: 12,
                  right: 12,
                  zIndex: 5
                }}
                data-no-pan="true"
              >
                <Paper
                  p="xs"
                  radius="lg"
                  className="surface-card"
                  style={{ display: 'flex', gap: 6 }}
                >
                  <ActionIcon
                    variant="light"
                    onClick={() => {
                      if (!viewportWidth || !viewportHeight) {
                        return;
                      }
                      zoomImage(1.1, viewportWidth / 2, viewportHeight / 2);
                    }}
                    aria-label={t('components.map.zoomIn')}
                  >
                    <IconZoomIn size={16} />
                  </ActionIcon>
                  <ActionIcon
                    variant="light"
                    onClick={() => {
                      if (!viewportWidth || !viewportHeight) {
                        return;
                      }
                      zoomImage(0.9, viewportWidth / 2, viewportHeight / 2);
                    }}
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
              </Box>
            </>
          )}
        </Box>
      </>
    );
  };

  const renderGeoMap = () => {
    const containerStyles: CSSProperties = {
      borderRadius: 16,
      border: '1px solid var(--app-border)',
      overflow: 'hidden',
      background: geoStyleUrl ? '#111' : 'transparent',
      height: isFullscreen ? 'calc(100vh - 160px)' : '100%',
      minHeight: 360,
      width: '100%',
      position: 'relative'
    };

    if (isFullscreen) {
      containerStyles.position = 'fixed';
      containerStyles.inset = '80px 64px';
      containerStyles.zIndex = 200;
      containerStyles.boxShadow = '0 16px 50px rgba(0,0,0,0.45)';
    }

    return (
      <>
        {isFullscreen && (
          <Box
            onClick={() => setIsFullscreen(false)}
            style={{
              position: 'fixed',
              inset: 0,
              background: 'rgba(5, 5, 5, 0.6)',
              zIndex: 199
            }}
          />
        )}
        <Box
          ref={mapContainerRef}
          onDragOver={(event) => event.preventDefault()}
          onDrop={handleGeoDrop}
          style={containerStyles}
        >
          {!geoStyleUrl && (
            <Group h="100%" align="center" justify="center">
              <Stack gap={4} align="center">
                <IconMapPin size={32} />
                <Text size="sm" className="muted-text">
                  {t('pages.maps.geo.missingStyle')}
                </Text>
              </Stack>
            </Group>
          )}
        </Box>
      </>
    );
  };

  const renderMapPanel = () => {
    if (!activeMap) {
      return renderEmptyMap(t('pages.maps.empty'));
    }

    return activeMap.type === 'Geo' ? renderGeoMap() : renderImageMap();
  };

  return (
    <Stack gap="lg" style={{ height: '100%', minHeight: 0 }}>
      <Group justify="space-between" align="center">
        <Stack gap={4}>
          <Text size="sm" className="muted-text">
            {t('pages.maps.subtitle')}
          </Text>
          <Text size="xl" fw={600}>
            {t('pages.maps.title')}
          </Text>
        </Stack>
        <Group gap="sm">
          {positionsDirty && (
            <Badge color="yellow" variant="light">
              {t('pages.maps.badges.unsaved')}
            </Badge>
          )}
          <Button
            variant="light"
            leftSection={<IconPlus size={16} />}
            onClick={createHandlers.open}
          >
            {t('pages.maps.actions.newMap')}
          </Button>
        </Group>
      </Group>

      <Group
        align="stretch"
        gap="md"
        style={{ flex: 1, minHeight: 0 }}
        wrap="nowrap"
      >
        <Paper
          p="md"
          radius="lg"
          className="surface-card"
          w={320}
          style={{ display: 'flex', flexDirection: 'column', minHeight: 0 }}
        >
          <Stack gap="md" style={{ flex: 1, minHeight: 0 }}>
            <Group justify="space-between" align="center">
              <Text size="sm" fw={600}>
                {t('pages.maps.sidebar.mapsTitle')}
              </Text>
              <ActionIcon
                variant="light"
                onClick={() => queryClient.invalidateQueries({ queryKey: ['maps'] })}
                aria-label={t('common.actions.refresh')}
              >
                <IconRefresh size={16} />
              </ActionIcon>
            </Group>
            <TextInput
              placeholder={t('pages.maps.sidebar.searchMapPlaceholder')}
              value={mapSearch}
              onChange={(event) => setMapSearch(event.currentTarget.value)}
              leftSection={<IconFilter size={16} />}
            />
            <ScrollArea type="auto" style={{ flex: 1, minHeight: 120 }}>
              <Stack gap="xs">
                {mapTree.length === 0 ? (
                  <Text size="xs" className="muted-text">
                    {t('pages.maps.sidebar.emptyMaps')}
                  </Text>
                ) : (
                  renderMapTree(mapTree)
                )}
              </Stack>
            </ScrollArea>
            <Divider />
            <Text size="sm" fw={600}>
              {t('pages.maps.sidebar.camerasTitle')}
            </Text>
            <Text size="xs" className="muted-text">
              {t('pages.maps.sidebar.camerasHint')}
            </Text>
            <TextInput
              placeholder={t('pages.maps.sidebar.searchCameraPlaceholder')}
              value={search}
              onChange={(event) => setSearch(event.currentTarget.value)}
              leftSection={<IconSearch size={16} />}
            />
            <ScrollArea h={300} type="auto">
              <Stack gap="xs">
                {filteredCameras.map((camera) => {
                  const isPlaced = positionsByCamera.has(camera.cameraId);
                  const isSelected = selectedCameraId === camera.cameraId;
                  const isDraggable = Boolean(activeMap);
                  return (
                    <Paper
                      key={camera.cameraId}
                      p="xs"
                      radius="md"
                      withBorder
                      draggable={isDraggable}
                      onDragStart={(event) => {
                        event.dataTransfer.setData('camera-id', camera.cameraId);
                      }}
                      onClick={() => setSelectedCameraId(camera.cameraId)}
                      style={{
                        cursor: isDraggable ? 'grab' : 'pointer',
                        borderColor: isSelected ? 'var(--app-brand)' : undefined
                      }}
                    >
                      <Group justify="space-between" align="flex-start" gap="xs" wrap="nowrap">
                        <Stack gap={2} style={{ flex: 1, minWidth: 0 }}>
                          <Text size="sm" fw={600}>
                            {camera.code?.trim() || t('pages.maps.sidebar.unnamedCamera')}
                          </Text>
                          <Text size="xs" className="muted-text">
                            {t('pages.maps.sidebar.cameraIp', { ip: camera.ipAddress })}
                          </Text>
                        </Stack>
                        {isPlaced && (
                          <Badge color="brand" variant="light">
                            {t('pages.maps.sidebar.badges.placed')}
                          </Badge>
                        )}
                      </Group>
                    </Paper>
                  );
                })}
              </Stack>
            </ScrollArea>
          </Stack>
        </Paper>

        <Paper
          p="md"
          radius="lg"
          className="surface-card"
          style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}
        >
          <Stack gap="md" style={{ flex: 1, minHeight: 0 }}>
            <Group justify="space-between" align="center">
              <Stack gap={4}>
                <Text size="sm" className="muted-text">
                  {activeMap?.type === 'Geo'
                    ? t('pages.maps.panel.geoHint')
                    : t('pages.maps.panel.imageHint')}
                </Text>
                <Group gap="sm">
                  <Text size="md" fw={600}>
                    {activeMap?.name ?? t('pages.maps.panel.noMapSelected')}
                  </Text>
                  {activeMap && (
                    <Badge color="brand" variant="light">
                      {resolveMapTypeLabel(activeMap.type)}
                    </Badge>
                  )}
                </Group>
              </Stack>
              <Group gap="sm">
                {activeMap?.type === 'Image' && (
                  <FileInput
                    placeholder={t('pages.maps.panel.uploadImage')}
                    accept="image/png,image/jpeg"
                    leftSection={<IconUpload size={16} />}
                    onChange={(file) => file && uploadImageMutation.mutate(file)}
                  />
                )}
                <Button
                  variant="light"
                  disabled={!canSave}
                  loading={savePositionsMutation.isPending}
                  onClick={() => savePositionsMutation.mutate(positions)}
                >
                  {t('pages.maps.actions.savePositions')}
                </Button>
              </Group>
            </Group>
            <Box style={{ flex: 1, minHeight: 0 }}>{renderMapPanel()}</Box>
          </Stack>
        </Paper>
      </Group>

      <Modal
        opened={createOpened}
        onClose={createHandlers.close}
        title={t('pages.maps.modals.create.title')}
        size="md"
      >
        <Stack gap="md">
          <TextInput
            label={t('pages.maps.fields.name')}
            placeholder={t('pages.maps.placeholders.mapName')}
            value={newMapName}
            onChange={(event) => setNewMapName(event.currentTarget.value)}
          />
          <Select
            label={t('pages.maps.fields.type')}
            data={[
              { value: 'Image', label: t('pages.maps.types.imageLabel') },
              { value: 'Geo', label: t('pages.maps.types.geoLabel') }
            ]}
            value={newMapType}
            onChange={(value) => setNewMapType((value as MapLayoutType) ?? 'Image')}
          />
          <Select
            label={t('pages.maps.fields.parent')}
            placeholder={t('pages.maps.placeholders.rootMap')}
            data={parentOptions}
            value={newMapParentId}
            onChange={setNewMapParentId}
            clearable
          />
          <Group justify="flex-end">
            <Button
              variant="light"
              onClick={() =>
                createMapMutation.mutate({
                  name: newMapName.trim(),
                  type: newMapType,
                  parentId: newMapParentId ?? undefined
                })
              }
              disabled={newMapName.trim().length === 0}
              loading={createMapMutation.isPending}
            >
              {t('common.actions.create')}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal
        opened={editOpened}
        onClose={editHandlers.close}
        title={t('pages.maps.modals.edit.title')}
        size="md"
      >
        <Stack gap="md">
          <TextInput
            label={t('pages.maps.fields.name')}
            value={editMapName}
            onChange={(event) => setEditMapName(event.currentTarget.value)}
          />
          <Select
            label={t('pages.maps.fields.type')}
            data={[
              { value: 'Image', label: t('pages.maps.types.imageLabel') },
              { value: 'Geo', label: t('pages.maps.types.geoLabel') }
            ]}
            value={editMapType}
            onChange={(value) => setEditMapType((value as MapLayoutType) ?? 'Image')}
          />
          <Select
            label={t('pages.maps.fields.parent')}
            placeholder={t('pages.maps.placeholders.rootMap')}
            data={editParentOptions}
            value={editMapParentId}
            onChange={setEditMapParentId}
            clearable
          />
          <Group justify="flex-end">
            <Button
              variant="light"
              onClick={() => {
                if (!editMap) {
                  return;
                }
                updateMapMutation.mutate({
                  id: editMap.id,
                  payload: {
                    name: editMapName.trim(),
                    type: editMapType,
                    parentId: editMapParentId ?? undefined
                  }
                });
              }}
              disabled={!editMap || editMapName.trim().length === 0}
              loading={updateMapMutation.isPending}
            >
              {t('common.actions.save')}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal
        opened={deleteOpened}
        onClose={deleteHandlers.close}
        title={t('pages.maps.modals.delete.title')}
        size="md"
      >
        <Stack gap="md">
          <Text size="sm">
            {t('pages.maps.modals.delete.message', { name: deleteTarget?.name ?? '' })}
          </Text>
          <Group justify="flex-end">
            <Button variant="default" onClick={deleteHandlers.close}>
              {t('common.actions.cancel')}
            </Button>
            <Button
              color="red"
              onClick={() => deleteTarget && deleteMapMutation.mutate(deleteTarget.id)}
              loading={deleteMapMutation.isPending}
            >
              {t('common.actions.delete')}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}









