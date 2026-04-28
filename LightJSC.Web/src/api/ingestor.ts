import { apiRequest } from './client';
import type {
  AlarmDeliverySettings,
  AttendanceSummaryRequest,
  AttendanceSummaryResponse,
  CameraRequest,
  CameraResponse,
  DiscoveredCameraResponse,
  EnrollFaceRequest,
  FaceDetectRequest,
  FaceDetectResponse,
  FaceEventRecord,
  FaceEventSearchRequest,
  FaceEventSearchResponse,
  FaceTraceEventRequest,
  FaceTraceImageRequest,
  FaceTracePersonRequest,
  FaceTemplateResponse,
  BestshotSettings,
  MatchingSettings,
  BestshotResponse,
  DashboardAlertResponse,
  DashboardCameraHealthResponse,
  DashboardMapHeatResponse,
  DashboardSummaryResponse,
  DashboardSystemMetricsResponse,
  DashboardTimeseriesResponse,
  DashboardTopResponse,
  MapCameraPositionRequest,
  MapDetailResponse,
  MapLayoutRequest,
  MapLayoutResponse,
  MapOptionsResponse,
  MapRouteRequest,
  MapRouteResponse,
  MapViewRequest,
  ReembedEventsRequest,
  ReembedResult,
  CreatePersonScanSessionRequest,
  PersonScanRequest,
  PersonScanResult,
  PersonRequest,
  PersonResponse,
  SubscriberRequest,
  SubscriberResponse,
  TestRtspResponse
} from './types';

export const listCameras = () => apiRequest<CameraResponse[]>('/api/v1/cameras');

export const createCamera = (payload: CameraRequest) =>
  apiRequest<CameraResponse>('/api/v1/cameras', {
    method: 'POST',
    body: JSON.stringify(payload)
  });

export const updateCamera = (cameraId: string, payload: CameraRequest) =>
  apiRequest<CameraResponse>(`/api/v1/cameras/${cameraId}`, {
    method: 'PUT',
    body: JSON.stringify(payload)
  });

export const deleteCamera = (cameraId: string) =>
  apiRequest<void>(`/api/v1/cameras/${cameraId}`, { method: 'DELETE' });

export const testCameraRtsp = (cameraId: string) =>
  apiRequest<TestRtspResponse>(`/api/v1/cameras/${cameraId}/test-rtsp`, { method: 'POST' });

export type DiscoverCamerasOptions = {
  timeoutSeconds?: number;
  ipStart?: string;
  ipEnd?: string;
};

export const discoverCameras = (options: DiscoverCamerasOptions = {}) => {
  const params = new URLSearchParams();
  params.set('timeoutSeconds', String(options.timeoutSeconds ?? 4));
  if (options.ipStart) {
    params.set('ipStart', options.ipStart);
  }
  if (options.ipEnd) {
    params.set('ipEnd', options.ipEnd);
  }

  return apiRequest<DiscoveredCameraResponse[]>(`/api/v1/cameras/discover?${params}`);
};

export const listSubscribers = () => apiRequest<SubscriberResponse[]>('/api/v1/subscribers');

export const createSubscriber = (payload: SubscriberRequest) =>
  apiRequest<SubscriberResponse>('/api/v1/subscribers', {
    method: 'POST',
    body: JSON.stringify(payload)
  });

export const updateSubscriber = (id: string, payload: SubscriberRequest) =>
  apiRequest<SubscriberResponse>(`/api/v1/subscribers/${id}`, {
    method: 'PUT',
    body: JSON.stringify(payload)
  });

export const deleteSubscriber = (id: string) =>
  apiRequest<void>(`/api/v1/subscribers/${id}`, { method: 'DELETE' });

export const listMaps = () => apiRequest<MapLayoutResponse[]>('/api/v1/maps');

export const getMapOptions = () => apiRequest<MapOptionsResponse>('/api/v1/maps/options');

export const getMap = (id: string) => apiRequest<MapDetailResponse>(`/api/v1/maps/${id}`);

export const createMap = (payload: MapLayoutRequest) =>
  apiRequest<MapLayoutResponse>('/api/v1/maps', {
    method: 'POST',
    body: JSON.stringify(payload)
  });

export const updateMap = (id: string, payload: MapLayoutRequest) =>
  apiRequest<MapLayoutResponse>(`/api/v1/maps/${id}`, {
    method: 'PUT',
    body: JSON.stringify(payload)
  });

export const deleteMap = (id: string) =>
  apiRequest<void>(`/api/v1/maps/${id}`, { method: 'DELETE' });

export const updateMapView = (id: string, payload: MapViewRequest) =>
  apiRequest<MapLayoutResponse>(`/api/v1/maps/${id}/view`, {
    method: 'PUT',
    body: JSON.stringify(payload)
  });

export const uploadMapImage = (id: string, file: File) => {
  const data = new FormData();
  data.append('file', file);
  return apiRequest<MapLayoutResponse>(`/api/v1/maps/${id}/image`, {
    method: 'POST',
    body: data
  });
};

export const saveMapCameras = (id: string, payload: MapCameraPositionRequest[]) =>
  apiRequest<MapDetailResponse>(`/api/v1/maps/${id}/cameras`, {
    method: 'PUT',
    body: JSON.stringify(payload)
  });

export const buildMapRoute = (payload: MapRouteRequest) =>
  apiRequest<MapRouteResponse>('/api/v1/maps/route', {
    method: 'POST',
    body: JSON.stringify(payload)
  });

export const listPersons = () => apiRequest<PersonResponse[]>('/api/v1/persons');

export const createPerson = (payload: PersonRequest) =>
  apiRequest<PersonResponse>('/api/v1/persons', {
    method: 'POST',
    body: JSON.stringify(payload)
  });

export const updatePerson = (personId: string, payload: PersonRequest) =>
  apiRequest<PersonResponse>(`/api/v1/persons/${personId}`, {
    method: 'PUT',
    body: JSON.stringify(payload)
  });

export const deletePerson = (personId: string) =>
  apiRequest<void>(`/api/v1/persons/${personId}`, { method: 'DELETE' });

export const listPersonTemplates = (personId: string) =>
  apiRequest<FaceTemplateResponse[]>(`/api/v1/persons/${personId}/templates`);

export const enrollPerson = (personId: string, payload: EnrollFaceRequest) =>
  apiRequest<FaceTemplateResponse>(`/api/v1/persons/${personId}/enroll`, {
    method: 'POST',
    body: JSON.stringify(payload)
  });

export const detectPersonFaces = (payload: FaceDetectRequest) =>
  apiRequest<FaceDetectResponse[]>('/api/v1/persons/face-detect', {
    method: 'POST',
    body: JSON.stringify(payload)
  });

export const deletePersonTemplate = (personId: string, templateId: string) =>
  apiRequest<void>(`/api/v1/persons/${personId}/templates/${templateId}`, {
    method: 'DELETE'
  });

export const updatePersonTemplateStatus = (
  personId: string,
  templateId: string,
  isActive: boolean
) =>
  apiRequest<FaceTemplateResponse>(`/api/v1/persons/${personId}/templates/${templateId}/status`, {
    method: 'PUT',
    body: JSON.stringify({ isActive })
  });

export const createPersonScanSession = (payload: CreatePersonScanSessionRequest) =>
  apiRequest<PersonScanResult>('/api/v1/person-scans/sessions', {
    method: 'POST',
    body: JSON.stringify(payload)
  });

export const scanPersonScanSession = (sessionId: string, payload: PersonScanRequest = {}) =>
  apiRequest<PersonScanResult>(`/api/v1/person-scans/sessions/${sessionId}/scan`, {
    method: 'POST',
    body: JSON.stringify(payload)
  });

export const getAlarmDeliverySettings = () =>
  apiRequest<AlarmDeliverySettings>('/api/v1/settings/alarm-delivery');

export const updateAlarmDeliverySettings = (payload: AlarmDeliverySettings) =>
  apiRequest<AlarmDeliverySettings>('/api/v1/settings/alarm-delivery', {
    method: 'PUT',
    body: JSON.stringify(payload)
  });

export const getMatchingSettings = () =>
  apiRequest<MatchingSettings>('/api/v1/settings/matching');

export const updateMatchingSettings = (payload: MatchingSettings) =>
  apiRequest<MatchingSettings>('/api/v1/settings/matching', {
    method: 'PUT',
    body: JSON.stringify(payload)
  });

export const getBestshotSettings = () =>
  apiRequest<BestshotSettings>('/api/v1/settings/bestshot');

export const updateBestshotSettings = (payload: BestshotSettings) =>
  apiRequest<BestshotSettings>('/api/v1/settings/bestshot', {
    method: 'PUT',
    body: JSON.stringify(payload)
  });

export const reembedEvents = (payload: ReembedEventsRequest) =>
  apiRequest<ReembedResult>('/api/v1/re-embed/events', {
    method: 'POST',
    body: JSON.stringify(payload)
  });

export const searchFaceEvents = (payload: FaceEventSearchRequest) =>
  apiRequest<FaceEventSearchResponse>('/api/v1/face-events/search', {
    method: 'POST',
    body: JSON.stringify(payload)
  });

export const getFaceEvent = (eventId: string, includeBestshot = false) =>
  apiRequest<FaceEventRecord>(`/api/v1/face-events/${eventId}?includeBestshot=${includeBestshot}`);

export const getFaceEventBestshot = (eventId: string) =>
  apiRequest<BestshotResponse>(`/api/v1/face-events/${eventId}/bestshot`);

export const traceFaceByPerson = (payload: FaceTracePersonRequest) =>
  apiRequest<FaceEventSearchResponse>('/api/v1/face-trace/person', {
    method: 'POST',
    body: JSON.stringify(payload)
  });

export const traceFaceByImage = (payload: FaceTraceImageRequest) =>
  apiRequest<FaceEventSearchResponse>('/api/v1/face-trace/image', {
    method: 'POST',
    body: JSON.stringify(payload)
  });

export const traceFaceByEvent = (eventId: string, payload: FaceTraceEventRequest) =>
  apiRequest<FaceEventSearchResponse>(`/api/v1/face-trace/event/${eventId}`, {
    method: 'POST',
    body: JSON.stringify(payload)
  });

export const getDashboardSummary = (params: { fromUtc?: string; toUtc?: string } = {}) => {
  const search = new URLSearchParams();
  if (params.fromUtc) search.set('fromUtc', params.fromUtc);
  if (params.toUtc) search.set('toUtc', params.toUtc);
  const query = search.toString();
  return apiRequest<DashboardSummaryResponse>(`/api/v1/dashboard/summary${query ? `?${query}` : ''}`);
};

export const getDashboardTimeseries = (params: {
  fromUtc?: string;
  toUtc?: string;
  stepSeconds?: number;
  metric?: string;
}) => {
  const search = new URLSearchParams();
  if (params.metric) search.set('metric', params.metric);
  if (params.fromUtc) search.set('fromUtc', params.fromUtc);
  if (params.toUtc) search.set('toUtc', params.toUtc);
  if (params.stepSeconds) search.set('stepSeconds', String(params.stepSeconds));
  const query = search.toString();
  return apiRequest<DashboardTimeseriesResponse>(
    `/api/v1/dashboard/timeseries${query ? `?${query}` : ''}`
  );
};

export const getDashboardTop = (params: {
  fromUtc?: string;
  toUtc?: string;
  metric?: string;
  groupBy?: string;
  limit?: number;
}) => {
  const search = new URLSearchParams();
  if (params.metric) search.set('metric', params.metric);
  if (params.groupBy) search.set('groupBy', params.groupBy);
  if (params.fromUtc) search.set('fromUtc', params.fromUtc);
  if (params.toUtc) search.set('toUtc', params.toUtc);
  if (params.limit) search.set('limit', String(params.limit));
  const query = search.toString();
  return apiRequest<DashboardTopResponse>(`/api/v1/dashboard/top${query ? `?${query}` : ''}`);
};

export const getDashboardCameraHealth = () =>
  apiRequest<DashboardCameraHealthResponse>('/api/v1/dashboard/cameras/health');

export const getDashboardAlerts = (params: {
  fromUtc?: string;
  toUtc?: string;
  cursor?: string;
  limit?: number;
}) => {
  const search = new URLSearchParams();
  if (params.fromUtc) search.set('fromUtc', params.fromUtc);
  if (params.toUtc) search.set('toUtc', params.toUtc);
  if (params.cursor) search.set('cursor', params.cursor);
  if (params.limit) search.set('limit', String(params.limit));
  const query = search.toString();
  return apiRequest<DashboardAlertResponse>(`/api/v1/dashboard/alerts${query ? `?${query}` : ''}`);
};

export const getDashboardMapHeat = (params: {
  fromUtc?: string;
  toUtc?: string;
  mapId?: string;
  bbox?: string;
}) => {
  const search = new URLSearchParams();
  if (params.fromUtc) search.set('fromUtc', params.fromUtc);
  if (params.toUtc) search.set('toUtc', params.toUtc);
  if (params.mapId) search.set('mapId', params.mapId);
  if (params.bbox) search.set('bbox', params.bbox);
  const query = search.toString();
  return apiRequest<DashboardMapHeatResponse>(`/api/v1/dashboard/map/heat${query ? `?${query}` : ''}`);
};

export const getDashboardSystemMetrics = () =>
  apiRequest<DashboardSystemMetricsResponse>('/api/v1/dashboard/system-metrics');

export const getAttendanceSummary = (payload: AttendanceSummaryRequest) =>
  apiRequest<AttendanceSummaryResponse>('/api/v1/attendance/summary', {
    method: 'POST',
    body: JSON.stringify(payload)
  });
