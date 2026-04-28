export interface CameraRequest {
  cameraId: string;
  code?: string | null;
  ipAddress: string;
  rtspUsername: string;
  rtspPassword?: string | null;
  rtspProfile: string;
  rtspPath: string;
  cameraSeries?: string | null;
  cameraModel?: string | null;
  enabled: boolean;
}

export interface CameraResponse {
  cameraId: string;
  code?: string | null;
  ipAddress: string;
  rtspUsername: string;
  rtspProfile: string;
  rtspPath: string;
  cameraSeries?: string | null;
  cameraModel?: string | null;
  enabled: boolean;
  hasPassword: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface TestRtspResponse {
  success: boolean;
}

export interface DiscoveredCameraResponse {
  deviceId?: string | null;
  ipAddress?: string | null;
  name?: string | null;
  model?: string | null;
  cameraSeries?: string | null;
  macAddress?: string | null;
  xAddr?: string | null;
  scopes?: string | null;
}

export interface SubscriberRequest {
  name: string;
  endpointUrl: string;
  enabled: boolean;
}

export interface SubscriberResponse {
  id: string;
  name: string;
  endpointUrl: string;
  enabled: boolean;
  createdAt: string;
}

export type MapLayoutType = 'Image' | 'Geo';

export interface MapLayoutRequest {
  parentId?: string | null;
  name: string;
  type: MapLayoutType;
}

export interface MapLayoutResponse {
  id: string;
  parentId?: string | null;
  name: string;
  type: MapLayoutType;
  imageUrl?: string | null;
  imageWidth?: number | null;
  imageHeight?: number | null;
  geoCenterLatitude?: number | null;
  geoCenterLongitude?: number | null;
  geoZoom?: number | null;
  createdAt: string;
  updatedAt: string;
}

export interface MapCameraPositionRequest {
  cameraId: string;
  label?: string | null;
  x?: number | null;
  y?: number | null;
  angleDegrees?: number | null;
  fovDegrees?: number | null;
  range?: number | null;
  iconScale?: number | null;
  latitude?: number | null;
  longitude?: number | null;
}

export interface MapCameraPositionResponse extends MapCameraPositionRequest {
  updatedAt: string;
}

export interface MapDetailResponse {
  map: MapLayoutResponse;
  cameras: MapCameraPositionResponse[];
}

export interface GeoPoint {
  latitude: number;
  longitude: number;
}

export interface MapRouteRequest {
  points: GeoPoint[];
  mode?: string;
}

export interface MapRouteResponse {
  points: GeoPoint[];
  isFallback: boolean;
}

export interface MapOptionsResponse {
  geoStyleUrl?: string | null;
  routingEnabled: boolean;
}

export interface MapViewRequest {
  geoCenterLatitude: number;
  geoCenterLongitude: number;
  geoZoom: number;
}

export interface PersonRequest {
  code: string;
  firstName: string;
  lastName: string;
  personalId?: string | null;
  documentNumber?: string | null;
  dateOfBirth?: string | null;
  dateOfIssue?: string | null;
  address?: string | null;
  rawQrPayload?: string | null;
  gender?: string | null;
  age?: number | null;
  remarks?: string | null;
  category?: string | null;
  listType?: string | null;
  isActive: boolean;
}

export interface PersonResponse {
  id: string;
  code: string;
  firstName: string;
  lastName: string;
  personalId?: string | null;
  documentNumber?: string | null;
  dateOfBirth?: string | null;
  dateOfIssue?: string | null;
  address?: string | null;
  rawQrPayload?: string | null;
  gender?: string | null;
  age?: number | null;
  remarks?: string | null;
  category?: string | null;
  listType?: string | null;
  isActive: boolean;
  isEnrolled: boolean;
  enrolledFaceImageBase64?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface FaceTemplateResponse {
  id: string;
  personId: string;
  featureVersion: string;
  l2Norm: number;
  sourceCameraId?: string | null;
  isActive: boolean;
  faceImageBase64?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface EnrollFaceRequest {
  cameraId: string;
  imageBase64: string;
  storeFaceImage: boolean;
  sourceCameraId?: string | null;
}

export interface FaceDetectRequest {
  imageBase64: string;
  maxFaces?: number;
}

export interface FaceDetectBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface FaceDetectResponse {
  faceId: string;
  score: number;
  box: FaceDetectBox;
  thumbnailBase64: string;
}

export interface PersonScanPerson {
  code?: string | null;
  personalId?: string | null;
  documentNumber?: string | null;
  fullName?: string | null;
  firstName?: string | null;
  lastName?: string | null;
  gender?: string | null;
  dateOfBirth?: string | null;
  dateOfIssue?: string | null;
  age?: number | null;
  address?: string | null;
  rawQrPayload?: string | null;
}

export interface CreatePersonScanSessionRequest {
  cameraId?: string | null;
}

export interface PersonScanRequest {
  mode?: string | null;
  resetQr?: boolean;
  resetFace?: boolean;
}

export interface PersonScanResult {
  sessionId: string;
  cameraId?: string | null;
  status: string;
  qrDetected: boolean;
  faceDetected: boolean;
  snapshotImageBase64?: string | null;
  faceImageBase64?: string | null;
  rawQrPayload?: string | null;
  person?: PersonScanPerson | null;
  errorMessage?: string | null;
  scannedAtUtc?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface PersonProfile {
  firstName?: string | null;
  lastName?: string | null;
  code?: string | null;
  category?: string | null;
  remarks?: string | null;
  listType?: string | null;
}

export interface BoundingBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface FaceEvent {
  id: string;
  cameraId: string;
  cameraCode?: string | null;
  cameraIp?: string | null;
  cameraName?: string | null;
  zone?: string | null;
  eventTimeUtc: string;
  receivedAtUtc: string;
  featureBase64?: string | null;
  l2Norm: number;
  featureVersion: string;
  age?: number | null;
  gender?: string | null;
  mask?: string | null;
  scoreText?: string | null;
  similarityText?: string | null;
  watchlistEntryId?: string | null;
  personId?: string | null;
  person?: PersonProfile | null;
  bbox?: BoundingBox | null;
  isKnown: boolean;
  faceImageBase64?: string | null;
}

export interface FaceEventSnapshot {
  known: FaceEvent[];
  unknown: FaceEvent[];
  knownTotal?: number;
  unknownTotal?: number;
}

export interface AlarmDeliverySettings {
  sendWhiteList: boolean;
  sendBlackList: boolean;
  sendProtect: boolean;
  sendUndefined: boolean;
}

export interface MatchingSettings {
  similarity: number;
  score: number;
}

export interface BestshotSettings {
  rootPath: string;
  retentionDays: number;
}

export interface ReembedEventsRequest {
  cameraId?: string;
  cameraIds?: string[];
  targetFeatureVersion?: string;
  featureVersionByCamera?: Record<string, string>;
  fromUtc?: string;
  toUtc?: string;
  maxEvents?: number;
  dryRun?: boolean;
}

export interface ReembedResult {
  processed: number;
  created: number;
  skipped: number;
  failed: number;
  errors?: string[];
}

export interface FaceEventSearchFilter {
  fromUtc?: string;
  toUtc?: string;
  cameraIds?: string[];
  isKnown?: boolean;
  listType?: string;
  gender?: string;
  ageMin?: number;
  ageMax?: number;
  mask?: string;
  scoreMin?: number;
  similarityMin?: number;
  personQuery?: string;
  personIds?: string[];
  category?: string;
  hasFeature?: boolean;
  watchlistEntryIds?: string[];
}

export interface FaceEventSearchRequest extends FaceEventSearchFilter {
  page?: number;
  pageSize?: number;
  includeBestshot?: boolean;
}

export interface FaceEventRecord {
  id: string;
  eventTimeUtc: string;
  cameraId: string;
  cameraIp?: string | null;
  cameraZone?: string | null;
  isKnown: boolean;
  watchlistEntryId?: string | null;
  personId?: string | null;
  person?: PersonProfile | null;
  similarity?: number | null;
  score?: number | null;
  bestshotBase64?: string | null;
  gender?: string | null;
  age?: number | null;
  mask?: string | null;
  hasFeature: boolean;
  traceSimilarity?: number | null;
}

export interface FaceEventSearchResponse {
  items: FaceEventRecord[];
  total: number;
}

export interface FaceTracePersonRequest {
  personId: string;
  topK: number;
  similarityMin?: number;
  includeBestshot?: boolean;
  filter?: FaceEventSearchFilter;
}

export interface FaceTraceImageRequest {
  cameraId?: string;
  imageBase64: string;
  topK: number;
  similarityMin?: number;
  includeBestshot?: boolean;
  filter?: FaceEventSearchFilter;
}

export interface FaceTraceEventRequest {
  topK: number;
  similarityMin?: number;
  includeBestshot?: boolean;
  filter?: FaceEventSearchFilter;
}

export interface BestshotResponse {
  base64?: string | null;
}

export interface DashboardSummaryResponse {
  fromUtc: string;
  toUtc: string;
  totalEvents: number;
  knownCount: number;
  unknownCount: number;
  matchCount: number;
  enabledCameras: number;
  disabledCameras: number;
  activeCameras: number;
  lastEventUtc?: string | null;
  generatedAtUtc: string;
}

export interface DashboardTimeseriesPoint {
  bucketStartUtc: string;
  totalCount: number;
  knownCount: number;
  unknownCount: number;
  matchCount: number;
  totalCumulative?: number;
  knownCumulative?: number;
  unknownCumulative?: number;
  matchCumulative?: number;
}

export interface DashboardTimeseriesResponse {
  fromUtc: string;
  toUtc: string;
  stepSeconds: number;
  points: DashboardTimeseriesPoint[];
}

export interface DashboardTopItem {
  key: string;
  label: string;
  count: number;
}

export interface DashboardTopResponse {
  items: DashboardTopItem[];
}

export type DashboardCameraState = 'OK' | 'WARN' | 'OFFLINE' | 'DISABLED';

export interface DashboardCameraHealthItem {
  cameraId: string;
  cameraCode?: string | null;
  ipAddress?: string | null;
  enabled: boolean;
  lastEventUtc?: string | null;
  events5m: number;
  state: DashboardCameraState;
}

export interface DashboardCameraHealthResponse {
  generatedAtUtc: string;
  items: DashboardCameraHealthItem[];
}

export type DashboardSeverity = 'CRITICAL' | 'HIGH' | 'MEDIUM' | 'LOW';

export interface DashboardAlertItem {
  id: string;
  eventTimeUtc: string;
  cameraId: string;
  cameraCode?: string | null;
  isKnown: boolean;
  watchlistEntryId?: string | null;
  personId?: string | null;
  personName?: string | null;
  listType?: string | null;
  similarity?: number | null;
  score?: number | null;
  severity: DashboardSeverity;
}

export interface DashboardAlertResponse {
  items: DashboardAlertItem[];
}

export interface DashboardMapHeatPoint {
  cameraId: string;
  label?: string | null;
  latitude: number;
  longitude: number;
  count: number;
}

export interface DashboardMapHeatResponse {
  points: DashboardMapHeatPoint[];
}

export interface DashboardSystemMetricsResponse {
  queueIngest?: number | null;
  queueWebhook?: number | null;
  ingestTotal?: number | null;
  ingestDroppedTotal?: number | null;
  ingestDroppedByReason?: Record<string, number>;
  webhookSuccessTotal?: number | null;
  webhookFailTotal?: number | null;
  watchlistSize?: number | null;
  matchLatencyP95Seconds?: number | null;
  generatedAtUtc: string;
}

export interface AttendanceSummaryRequest {
  year: number;
  month: number;
  cameraIds?: string[];
  categories?: string[];
  personIds?: string[];
}

export interface AttendanceEventPoint {
  eventId: string;
  eventTimeUtc: string;
}

export interface AttendanceSummaryDayCell {
  day: number;
  inEvent?: AttendanceEventPoint | null;
  outEvent?: AttendanceEventPoint | null;
}

export interface AttendanceSummaryPersonRow {
  personId: string;
  fullName: string;
  personalId?: string | null;
  category?: string | null;
  days: AttendanceSummaryDayCell[];
}

export interface AttendanceSummaryResponse {
  year: number;
  month: number;
  daysInMonth: number;
  generatedAtUtc: string;
  items: AttendanceSummaryPersonRow[];
}
