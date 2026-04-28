export type SizingPresetId =
  | 'office'
  | 'lobby'
  | 'gate'
  | 'factory'
  | 'school'
  | 'retail';

export type SizingTier = 'pilot' | 'standard' | 'large' | 'enterprise';

export type WarningSeverity = 'info' | 'warn' | 'critical';

export type ArchitectureMode =
  | 'allInOne'
  | 'splitDatabase'
  | 'splitRealtime'
  | 'requiresArchitectureV2';

export type ClientRole = 'operator' | 'welcome' | 'enrollment' | 'admin' | 'video';

export type WarningCode =
  | 'singleActiveNode'
  | 'splitDatabaseRecommended'
  | 'splitRealtimeRecommended'
  | 'standbyRecommended'
  | 'httpsRequiredForEnrollment'
  | 'retentionHigh'
  | 'storageHigh'
  | 'eventRateHigh'
  | 'architectureLimit'
  | 'watchlistLarge'
  | 'liveViewNeedsSeparateSizing';

export interface SizingInputs {
  presetId: SizingPresetId;
  siteCount: number;
  cameraCount: number;
  avgEventsPerMinute: number;
  peakEventsPerMinute: number;
  activeHoursPerDay: number;
  knownRatio: number;
  personCount: number;
  templatesPerPerson: number;
  activeTemplateRatio: number;
  retentionDays: number;
  saveKnown: boolean;
  saveUnknown: boolean;
  saveThumb: boolean;
  avgBestshotKb: number;
  avgDbKb: number;
  avgEventPayloadKb: number;
  avgRealtimePayloadKb: number;
  dashboardUsers: number;
  faceStreamUsers: number;
  welcomeUsers: number;
  attendanceUsers: number;
  enrollmentStations: number;
  adminUsers: number;
  liveVideoOperators: number;
  webhookSubscribers: number;
  growthPercent12m: number;
  standbyNode: boolean;
  futureLiveView: boolean;
}

export interface SizingPreset {
  id: SizingPresetId;
  avgEventsPerMinute: number;
  peakEventsPerMinute: number;
  activeHoursPerDay: number;
}

export interface SizingMetrics {
  avgEps: number;
  peakEps: number;
  eventsPerDay: number;
  storedEventsPerDay: number;
  activeTemplates: number;
  bestshotGbPerDay: number;
  databaseGbPerDay: number;
  totalStorageGbPerDay: number;
  usableStorageTb: number;
  ingestMbps: number;
  realtimeMbps: number;
  totalRealtimeUsers: number;
  totalWebUsers: number;
  recommendedNicGbps: number;
}

export interface NodeRecommendation {
  role: 'app' | 'database' | 'realtime' | 'standby';
  count: number;
  cpuCores: number;
  ramGb: number;
  storageGb: number;
  systemStorageGb: number;
  dataStorageGb: number;
  dataStorageLabel: 'bestshot' | 'database' | 'mixed' | 'none';
  notes: string[];
}

export interface ClientRecommendation {
  role: ClientRole;
  count: number;
  cpuCores: number;
  cpuLabel: string;
  ramGb: number;
  storageGb: number;
  notes: string[];
}

export interface WarningRecommendation {
  code: WarningCode;
  severity: WarningSeverity;
}

export interface BomItem {
  key: string;
  quantity: number;
  title: string;
  spec: string;
}

export interface ProjectionSummary {
  cameraCount: number;
  peakEps: number;
  usableStorageTb: number;
  tier: SizingTier;
}

export interface SizingResult {
  tier: SizingTier;
  architecture: ArchitectureMode;
  metrics: SizingMetrics;
  nodes: NodeRecommendation[];
  clients: ClientRecommendation[];
  warnings: WarningRecommendation[];
  bom: BomItem[];
  projection12m: ProjectionSummary;
}

export const SIZING_PRESETS: SizingPreset[] = [
  { id: 'office', avgEventsPerMinute: 0.8, peakEventsPerMinute: 3, activeHoursPerDay: 10 },
  { id: 'lobby', avgEventsPerMinute: 1.5, peakEventsPerMinute: 6, activeHoursPerDay: 12 },
  { id: 'gate', avgEventsPerMinute: 3, peakEventsPerMinute: 10, activeHoursPerDay: 14 },
  { id: 'factory', avgEventsPerMinute: 1.2, peakEventsPerMinute: 5, activeHoursPerDay: 16 },
  { id: 'school', avgEventsPerMinute: 1, peakEventsPerMinute: 6, activeHoursPerDay: 12 },
  { id: 'retail', avgEventsPerMinute: 2, peakEventsPerMinute: 8, activeHoursPerDay: 12 }
];

export const DEFAULT_SIZING_INPUTS: SizingInputs = {
  presetId: 'office',
  siteCount: 1,
  cameraCount: 30,
  avgEventsPerMinute: 0.8,
  peakEventsPerMinute: 3,
  activeHoursPerDay: 10,
  knownRatio: 0.35,
  personCount: 1000,
  templatesPerPerson: 2,
  activeTemplateRatio: 1,
  retentionDays: 90,
  saveKnown: true,
  saveUnknown: true,
  saveThumb: false,
  avgBestshotKb: 25,
  avgDbKb: 8,
  avgEventPayloadKb: 28,
  avgRealtimePayloadKb: 25,
  dashboardUsers: 2,
  faceStreamUsers: 1,
  welcomeUsers: 1,
  attendanceUsers: 1,
  enrollmentStations: 1,
  adminUsers: 1,
  liveVideoOperators: 0,
  webhookSubscribers: 2,
  growthPercent12m: 25,
  standbyNode: false,
  futureLiveView: false
};

const round = (value: number, digits = 2) => {
  const factor = 10 ** digits;
  return Math.round(value * factor) / factor;
};

const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max);
const DATA_STORAGE_SAFETY_FACTOR = 2.4;
const ALL_IN_ONE_SYSTEM_RESERVE_GB = 384;
const APP_SYSTEM_RESERVE_GB = 256;
const DATABASE_SYSTEM_RESERVE_GB = 512;
const REALTIME_SYSTEM_RESERVE_GB = 256;

const roundStorageGb = (requiredGb: number, minGb: number, stepGb = 256) => {
  const normalized = Math.max(minGb, Math.ceil(requiredGb));
  return Math.ceil(normalized / stepGb) * stepGb;
};

export const applyPresetValues = (
  inputs: SizingInputs,
  presetId: SizingPresetId
): SizingInputs => {
  const preset = SIZING_PRESETS.find((item) => item.id === presetId);
  if (!preset) {
    return inputs;
  }

  return {
    ...inputs,
    presetId,
    avgEventsPerMinute: preset.avgEventsPerMinute,
    peakEventsPerMinute: preset.peakEventsPerMinute,
    activeHoursPerDay: preset.activeHoursPerDay
  };
};

const resolveTier = (cameraCount: number, peakEps: number): SizingTier => {
  if (cameraCount <= 30 && peakEps <= 2) {
    return 'pilot';
  }

  if (cameraCount <= 100 && peakEps <= 8) {
    return 'standard';
  }

  if (cameraCount <= 300 && peakEps <= 20) {
    return 'large';
  }

  return 'enterprise';
};

const resolveArchitecture = (
  tier: SizingTier,
  metrics: SizingMetrics,
  inputs: SizingInputs
): ArchitectureMode => {
  if (tier === 'enterprise' || metrics.peakEps > 20 || inputs.cameraCount > 300) {
    return 'requiresArchitectureV2';
  }

  if (metrics.totalRealtimeUsers > 10 || inputs.webhookSubscribers > 20) {
    return 'splitRealtime';
  }

  if (tier !== 'pilot' || metrics.activeTemplates > 10000 || metrics.peakEps > 4) {
    return 'splitDatabase';
  }

  return 'allInOne';
};

const buildNodes = (
  tier: SizingTier,
  architecture: ArchitectureMode,
  inputs: SizingInputs,
  metrics: SizingMetrics
): NodeRecommendation[] => {
  const nodes: NodeRecommendation[] = [];
  const requiredStorageGb = metrics.usableStorageTb * 1024;
  const bestshotDataGb = roundStorageGb(
    metrics.bestshotGbPerDay * inputs.retentionDays * DATA_STORAGE_SAFETY_FACTOR,
    128,
    32
  );
  const databaseDataGb = roundStorageGb(
    metrics.databaseGbPerDay * inputs.retentionDays * DATA_STORAGE_SAFETY_FACTOR,
    128,
    32
  );

  if (tier === 'pilot' && architecture === 'allInOne') {
    const systemStorageGb = ALL_IN_ONE_SYSTEM_RESERVE_GB;
    const dataStorageGb = roundStorageGb(bestshotDataGb + databaseDataGb, 128, 32);
    nodes.push({
      role: 'app',
      count: 1,
      cpuCores: 16,
      ramGb: 32,
      storageGb: systemStorageGb + dataStorageGb,
      systemStorageGb,
      dataStorageGb,
      dataStorageLabel: 'mixed',
      notes: ['allInOne', 'metadataOnly']
    });
  } else {
    const appNode =
      tier === 'standard'
        ? { cpuCores: 16, ramGb: 64, storageGb: 256 }
        : tier === 'large'
          ? { cpuCores: 24, ramGb: 64, storageGb: 512 }
          : { cpuCores: 32, ramGb: 128, storageGb: 1024 };

    const dbNode =
      tier === 'standard'
        ? {
            cpuCores: 16,
            ramGb: 64,
            storageGb: roundStorageGb(requiredStorageGb * 1.2, 128, 32)
          }
        : tier === 'large'
          ? {
              cpuCores: 32,
              ramGb: 128,
              storageGb: roundStorageGb(requiredStorageGb * 1.2, 256, 64)
            }
          : {
              cpuCores: 48,
              ramGb: 256,
              storageGb: roundStorageGb(requiredStorageGb * 1.2, 512, 128)
            };

    nodes.push({
      role: 'app',
      count: 1,
      cpuCores: appNode.cpuCores,
      ramGb: appNode.ramGb,
      storageGb: roundStorageGb(appNode.storageGb + APP_SYSTEM_RESERVE_GB, 512, 64) + bestshotDataGb,
      systemStorageGb: roundStorageGb(appNode.storageGb + APP_SYSTEM_RESERVE_GB, 512, 64),
      dataStorageGb: bestshotDataGb,
      dataStorageLabel: 'bestshot',
      notes: ['activeCore', 'singleActiveOnly']
    });

    nodes.push({
      role: 'database',
      count: 1,
      cpuCores: dbNode.cpuCores,
      ramGb: dbNode.ramGb,
      storageGb: roundStorageGb(dbNode.storageGb + DATABASE_SYSTEM_RESERVE_GB, 1024, 128) + databaseDataGb,
      systemStorageGb: roundStorageGb(dbNode.storageGb + DATABASE_SYSTEM_RESERVE_GB, 1024, 128),
      dataStorageGb: databaseDataGb,
      dataStorageLabel: 'database',
      notes: ['pgvectorHeavy', 'nvmeRequired']
    });

    if (architecture === 'splitRealtime' || architecture === 'requiresArchitectureV2') {
      const systemStorageGb = roundStorageGb(REALTIME_SYSTEM_RESERVE_GB, 256, 64);
      nodes.push({
        role: 'realtime',
        count: 1,
        cpuCores: tier === 'standard' ? 8 : 12,
        ramGb: tier === 'standard' ? 16 : 32,
        storageGb: systemStorageGb,
        systemStorageGb,
        dataStorageGb: 0,
        dataStorageLabel: 'none',
        notes: ['signalrWebhook', 'optionalForIsolation']
      });
    }
  }

  if (inputs.standbyNode) {
    const primaryApp = nodes.find((item) => item.role === 'app');
    if (primaryApp) {
      nodes.push({
        role: 'standby',
        count: 1,
        cpuCores: primaryApp.cpuCores,
        ramGb: primaryApp.ramGb,
        storageGb: primaryApp.storageGb,
        systemStorageGb: primaryApp.systemStorageGb,
        dataStorageGb: primaryApp.dataStorageGb,
        dataStorageLabel: primaryApp.dataStorageLabel,
        notes: ['warmStandby', 'doNotRunActiveActive']
      });
    }
  }

  return nodes;
};

const buildClients = (inputs: SizingInputs): ClientRecommendation[] => {
  const clients: ClientRecommendation[] = [];

  if (inputs.dashboardUsers + inputs.faceStreamUsers + inputs.attendanceUsers > 0) {
    clients.push({
      role: 'operator',
      count: inputs.dashboardUsers + inputs.faceStreamUsers + inputs.attendanceUsers,
      cpuCores: 4,
      cpuLabel: 'Core i3 / Ryzen 3',
      ramGb: 8,
      storageGb: 128,
      notes: ['eventUi', 'fhdRecommended']
    });
  }

  if (inputs.welcomeUsers > 0) {
    clients.push({
      role: 'welcome',
      count: inputs.welcomeUsers,
      cpuCores: 4,
      cpuLabel: 'Intel N100 / Core i3',
      ramGb: 8,
      storageGb: 128,
      notes: ['kioskMode', 'wiredLan']
    });
  }

  if (inputs.enrollmentStations > 0) {
    clients.push({
      role: 'enrollment',
      count: inputs.enrollmentStations,
      cpuCores: 6,
      cpuLabel: 'Core i5 / Ryzen 5',
      ramGb: 16,
      storageGb: 256,
      notes: ['usbCamera', 'httpsRequired']
    });
  }

  if (inputs.adminUsers > 0) {
    clients.push({
      role: 'admin',
      count: inputs.adminUsers,
      cpuCores: 6,
      cpuLabel: 'Core i5 / Ryzen 5',
      ramGb: 16,
      storageGb: 256,
      notes: ['reportsTrace', 'exportWorkloads']
    });
  }

  if (inputs.futureLiveView || inputs.liveVideoOperators > 0) {
    clients.push({
      role: 'video',
      count: Math.max(1, inputs.liveVideoOperators),
      cpuCores: 8,
      cpuLabel: 'Core i7 / Ryzen 7',
      ramGb: 32,
      storageGb: 512,
      notes: ['browserDecode', 'separateAssessment']
    });
  }

  return clients;
};

const buildWarnings = (
  tier: SizingTier,
  architecture: ArchitectureMode,
  inputs: SizingInputs,
  metrics: SizingMetrics
): WarningRecommendation[] => {
  const warnings: WarningRecommendation[] = [
    { code: 'singleActiveNode', severity: 'info' }
  ];

  if (architecture !== 'allInOne') {
    warnings.push({ code: 'splitDatabaseRecommended', severity: 'warn' });
  }

  if (architecture === 'splitRealtime') {
    warnings.push({ code: 'splitRealtimeRecommended', severity: 'warn' });
  }

  if ((tier === 'large' || tier === 'enterprise' || metrics.peakEps > 8) && !inputs.standbyNode) {
    warnings.push({ code: 'standbyRecommended', severity: 'warn' });
  }

  if (inputs.enrollmentStations > 0) {
    warnings.push({ code: 'httpsRequiredForEnrollment', severity: 'info' });
  }

  if (inputs.retentionDays > 90) {
    warnings.push({ code: 'retentionHigh', severity: 'warn' });
  }

  if (metrics.usableStorageTb >= 4) {
    warnings.push({ code: 'storageHigh', severity: 'warn' });
  }

  if (metrics.peakEps > 15) {
    warnings.push({ code: 'eventRateHigh', severity: 'critical' });
  }

  if (architecture === 'requiresArchitectureV2') {
    warnings.push({ code: 'architectureLimit', severity: 'critical' });
  }

  if (metrics.activeTemplates > 50000) {
    warnings.push({ code: 'watchlistLarge', severity: 'warn' });
  }

  if (inputs.futureLiveView || inputs.liveVideoOperators > 0) {
    warnings.push({ code: 'liveViewNeedsSeparateSizing', severity: 'warn' });
  }

  return warnings;
};

const buildBom = (nodes: NodeRecommendation[], clients: ClientRecommendation[]): BomItem[] => {
  const serverItems = nodes.map((node) => ({
    key: `node-${node.role}`,
    quantity: node.count,
    title:
      node.role === 'app'
        ? 'App/Ingest node'
        : node.role === 'database'
          ? 'PostgreSQL/pgvector node'
          : node.role === 'realtime'
            ? 'Realtime/Webhook node'
            : 'Standby node',
    spec: `${node.cpuCores} cores / ${node.ramGb} GB RAM / ${node.storageGb} GB NVMe`
  }));

  const clientItems = clients.map((client) => ({
    key: `client-${client.role}`,
    quantity: client.count,
    title:
      client.role === 'operator'
        ? 'Standard operator station'
        : client.role === 'welcome'
          ? 'Welcome kiosk'
          : client.role === 'enrollment'
            ? 'Enrollment station'
            : client.role === 'admin'
              ? 'Admin/report station'
              : 'Live video station',
    spec: `${client.cpuLabel} / ${client.ramGb} GB RAM / ${client.storageGb} GB SSD`
  }));

  return [...serverItems, ...clientItems].filter((item) => item.quantity > 0);
};

export const calculateSizing = (rawInputs: SizingInputs): SizingResult => {
  const inputs: SizingInputs = {
    ...rawInputs,
    siteCount: Math.max(1, rawInputs.siteCount),
    cameraCount: Math.max(0, rawInputs.cameraCount),
    avgEventsPerMinute: Math.max(0, rawInputs.avgEventsPerMinute),
    peakEventsPerMinute: Math.max(rawInputs.avgEventsPerMinute, rawInputs.peakEventsPerMinute),
    activeHoursPerDay: clamp(rawInputs.activeHoursPerDay, 1, 24),
    knownRatio: clamp(rawInputs.knownRatio, 0, 1),
    personCount: Math.max(0, rawInputs.personCount),
    templatesPerPerson: Math.max(1, rawInputs.templatesPerPerson),
    activeTemplateRatio: clamp(rawInputs.activeTemplateRatio, 0.05, 1),
    retentionDays: Math.max(1, rawInputs.retentionDays),
    avgBestshotKb: Math.max(1, rawInputs.avgBestshotKb),
    avgDbKb: Math.max(1, rawInputs.avgDbKb),
    avgEventPayloadKb: Math.max(1, rawInputs.avgEventPayloadKb),
    avgRealtimePayloadKb: Math.max(1, rawInputs.avgRealtimePayloadKb),
    growthPercent12m: Math.max(0, rawInputs.growthPercent12m)
  };

  const avgEps = (inputs.cameraCount * inputs.avgEventsPerMinute) / 60;
  const peakEps = (inputs.cameraCount * inputs.peakEventsPerMinute) / 60;
  const eventsPerDay = inputs.cameraCount * inputs.avgEventsPerMinute * 60 * inputs.activeHoursPerDay;
  const storedRatio =
    (inputs.saveKnown ? inputs.knownRatio : 0) +
    (inputs.saveUnknown ? 1 - inputs.knownRatio : 0);
  const storedEventsPerDay = eventsPerDay * storedRatio;
  const activeTemplates =
    inputs.personCount * inputs.templatesPerPerson * clamp(inputs.activeTemplateRatio, 0, 1);
  const bestshotKbPerEvent = inputs.avgBestshotKb + (inputs.saveThumb ? 4 : 0);
  const bestshotGbPerDay = (storedEventsPerDay * bestshotKbPerEvent) / 1024 / 1024;
  const databaseGbPerDay = (storedEventsPerDay * inputs.avgDbKb) / 1024 / 1024;
  const totalStorageGbPerDay = bestshotGbPerDay + databaseGbPerDay;
  const usableStorageTb =
    (totalStorageGbPerDay * inputs.retentionDays * DATA_STORAGE_SAFETY_FACTOR) / 1024;
  const totalRealtimeUsers = inputs.faceStreamUsers + inputs.welcomeUsers;
  const totalWebUsers =
    inputs.dashboardUsers +
    inputs.faceStreamUsers +
    inputs.welcomeUsers +
    inputs.attendanceUsers +
    inputs.adminUsers;
  const ingestMbps = (avgEps * inputs.avgEventPayloadKb * 8) / 1024;
  const realtimeMbps = (peakEps * totalRealtimeUsers * inputs.avgRealtimePayloadKb * 8) / 1024;
  const totalTrafficMbps = ingestMbps + realtimeMbps;
  const recommendedNicGbps = totalTrafficMbps > 600 || usableStorageTb >= 4 ? 10 : 1;

  const metrics: SizingMetrics = {
    avgEps: round(avgEps, 2),
    peakEps: round(peakEps, 2),
    eventsPerDay: Math.round(eventsPerDay),
    storedEventsPerDay: Math.round(storedEventsPerDay),
    activeTemplates: Math.round(activeTemplates),
    bestshotGbPerDay: round(bestshotGbPerDay, 2),
    databaseGbPerDay: round(databaseGbPerDay, 2),
    totalStorageGbPerDay: round(totalStorageGbPerDay, 2),
    usableStorageTb: round(usableStorageTb, 2),
    ingestMbps: round(ingestMbps, 2),
    realtimeMbps: round(realtimeMbps, 2),
    totalRealtimeUsers,
    totalWebUsers,
    recommendedNicGbps
  };

  const tier = resolveTier(inputs.cameraCount, metrics.peakEps);
  const architecture = resolveArchitecture(tier, metrics, inputs);
  const nodes = buildNodes(tier, architecture, inputs, metrics);
  const clients = buildClients(inputs);
  const warnings = buildWarnings(tier, architecture, inputs, metrics);
  const bom = buildBom(nodes, clients);

  const projectedCameraCount = Math.ceil(inputs.cameraCount * (1 + inputs.growthPercent12m / 100));
  const projectedPeakEps = (projectedCameraCount * inputs.peakEventsPerMinute) / 60;
  const projectedEventsPerDay =
    projectedCameraCount * inputs.avgEventsPerMinute * 60 * inputs.activeHoursPerDay;
  const projectedStoredEventsPerDay = projectedEventsPerDay * storedRatio;
  const projectedStorageGbPerDay =
    (projectedStoredEventsPerDay * (bestshotKbPerEvent + inputs.avgDbKb)) / 1024 / 1024;
  const projectedUsableStorageTb =
    (projectedStorageGbPerDay * inputs.retentionDays * DATA_STORAGE_SAFETY_FACTOR) / 1024;
  const projection12m: ProjectionSummary = {
    cameraCount: projectedCameraCount,
    peakEps: round(projectedPeakEps, 2),
    usableStorageTb: round(projectedUsableStorageTb, 2),
    tier: resolveTier(projectedCameraCount, projectedPeakEps)
  };

  return {
    tier,
    architecture,
    metrics,
    nodes,
    clients,
    warnings,
    bom,
    projection12m
  };
};
