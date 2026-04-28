import {
  Alert,
  Badge,
  Box,
  Button,
  Divider,
  Group,
  Loader,
  Modal,
  Paper,
  Radio,
  Select,
  SimpleGrid,
  Stack,
  Text,
  Textarea
} from '@mantine/core';
import {
  IconAlertCircle,
  IconCamera,
  IconPlayerPlay,
  IconPlayerStop,
  IconQrcode,
  IconRefresh,
} from '@tabler/icons-react';
import { type ReactNode, useEffect, useMemo, useRef, useState } from 'react';
import jsQR from 'jsqr';
import { createPersonScanSession, scanPersonScanSession } from '../../api/ingestor';
import type { PersonScanPerson, PersonScanResult } from '../../api/types';
import { parseVneidQrPayload } from '../../utils/personScan';

type Translate = (key: string, params?: Record<string, string | number>, fallback?: string) => string;
type ScanSource = 'system' | 'local';
type LocalStatus = 'Previewing' | 'Step1Qr' | 'Step2Face' | 'Ready' | 'Error';

type CameraOption = {
  value: string;
  label: string;
};

export type PersonScanUsePayload = {
  person: PersonScanPerson;
  faceImageBase64: string;
  source: ScanSource;
  systemCameraId?: string | null;
  rawQrPayload?: string | null;
};

type PersonScanModalProps = {
  opened: boolean;
  onClose: () => void;
  onUseInForm: (payload: PersonScanUsePayload) => void | Promise<void>;
  cameraOptions: CameraOption[];
  t: Translate;
};

type MediaDeviceInfoOption = {
  value: string;
  label: string;
};

type BarcodeResult = {
  rawValue?: string;
};

type BarcodeDetectorLike = {
  detect: (source: CanvasImageSource) => Promise<BarcodeResult[]>;
};

type BarcodeDetectorConstructor = new (options?: { formats?: string[] }) => BarcodeDetectorLike;

type DetectedFaceLike = {
  boundingBox: DOMRectReadOnly;
};

type FaceDetectorLike = {
  detect: (source: CanvasImageSource) => Promise<DetectedFaceLike[]>;
};

type FaceDetectorConstructor = new (options?: { fastMode?: boolean; maxDetectedFaces?: number }) => FaceDetectorLike;

type DetectorWindow = Window & {
  BarcodeDetector?: BarcodeDetectorConstructor;
  FaceDetector?: FaceDetectorConstructor;
};

export function PersonScanModal({
  opened,
  onClose,
  onUseInForm,
  cameraOptions,
  t
}: PersonScanModalProps) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const scanTimerRef = useRef<number | null>(null);
  const systemTimerRef = useRef<number | null>(null);
  const localStatusRef = useRef<LocalStatus>('Previewing');
  const localLoopVersionRef = useRef(0);
  const faceImageRef = useRef<string | null>(null);
  const sessionIdRef = useRef<string | null>(null);
  const sessionPromiseRef = useRef<Promise<string> | null>(null);
  const systemLoopVersionRef = useRef(0);
  const systemLoopModeRef = useRef<'idle' | 'preview' | 'step1' | 'step2'>('idle');

  const [scanSource, setScanSource] = useState<ScanSource>('system');
  const [systemCameraId, setSystemCameraId] = useState<string>(cameraOptions[0]?.value ?? '');
  const [localDeviceId, setLocalDeviceId] = useState<string>('');
  const [localDevices, setLocalDevices] = useState<MediaDeviceInfoOption[]>([]);
  const [localStatus, setLocalStatus] = useState<LocalStatus>('Previewing');
  const [localError, setLocalError] = useState<string | null>(null);
  const [faceHint, setFaceHint] = useState<string | null>(null);
  const [cameraEnabled, setCameraEnabled] = useState(false);
  const [localBusy, setLocalBusy] = useState(false);
  const [systemBusy, setSystemBusy] = useState(false);
  const [systemResult, setSystemResult] = useState<PersonScanResult | null>(null);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [parsedPerson, setParsedPerson] = useState<PersonScanPerson | null>(null);
  const [rawQrPayload, setRawQrPayload] = useState<string | null>(null);
  const [faceImageBase64, setFaceImageBase64] = useState<string | null>(null);

  useEffect(() => {
    if (!opened) {
      resetModalState();
      return;
    }

    setSystemCameraId((current) => current || cameraOptions[0]?.value || '');
    void refreshLocalDevices();
  }, [opened, cameraOptions]);

  useEffect(() => {
    if (!opened) {
      return;
    }

    stopLoops();
    setLocalError(null);
    setFaceHint(null);
    setSystemResult(null);
    setSessionId(null);
    sessionIdRef.current = null;
    sessionPromiseRef.current = null;
    setParsedPerson(null);
    setRawQrPayload(null);
    setFaceImageBase64(null);
    updateLocalStatus('Previewing');
    if (scanSource !== 'local') {
      void stopCamera();
    }
  }, [scanSource, opened]);

  useEffect(() => {
    localStatusRef.current = localStatus;
  }, [localStatus]);

  useEffect(() => {
    sessionIdRef.current = sessionId;
  }, [sessionId]);

  useEffect(() => {
    faceImageRef.current = faceImageBase64;
  }, [faceImageBase64]);

  useEffect(() => {
    if (!opened || scanSource !== 'system' || !systemCameraId) {
      stopSystemLoop();
      return;
    }

    return () => {
      stopSystemLoop();
    };
  }, [opened, scanSource, systemCameraId]);

  const scanSourceOptions = useMemo(
    () => [
      { value: 'system', label: t('pages.persons.scan.sources.system', undefined, 'System IP camera') },
      { value: 'local', label: t('pages.persons.scan.sources.local', undefined, 'Internal / USB camera') }
    ],
    [t]
  );

  const canUseInForm = Boolean(parsedPerson && faceImageBase64);
  const activePerson = parsedPerson ?? systemResult?.person ?? null;
  const activeRawQrPayload = rawQrPayload ?? systemResult?.rawQrPayload ?? null;
  const previewStatusLabel =
    scanSource === 'local'
      ? cameraEnabled
        ? t('pages.persons.scan.local.previewOn', undefined, 'Camera enabled')
        : t('pages.persons.scan.local.previewOff', undefined, 'Camera stopped')
      : systemResult?.snapshotImageBase64
        ? t('pages.persons.scan.system.snapshotReady', undefined, 'Snapshot ready')
        : t('pages.persons.scan.system.snapshotWaiting', undefined, 'Waiting for snapshot');
  const previewStatusColor =
    scanSource === 'local'
      ? cameraEnabled
        ? 'brand'
        : 'gray'
      : systemResult?.snapshotImageBase64
        ? 'brand'
        : 'gray';
  const qrReady = Boolean(rawQrPayload);
  const faceReady = Boolean(faceImageBase64);
  const stepLabel =
    localStatus === 'Step1Qr'
      ? t('pages.persons.scan.local.step1Title', undefined, 'Step 1 - Read QR')
      : localStatus === 'Step2Face'
        ? t('pages.persons.scan.local.step2Title', undefined, 'Step 2 - Capture live face')
        : localStatus === 'Ready'
          ? t('pages.persons.scan.local.readyState', undefined, 'Ready to use')
          : t('pages.persons.scan.local.preview', undefined, 'Live preview');
  const previewGuideTitle =
    localStatus === 'Step1Qr'
      ? t('pages.persons.scan.local.step1Title', undefined, 'Step 1 - Read QR')
      : localStatus === 'Step2Face'
        ? t('pages.persons.scan.local.step2Title', undefined, 'Step 2 - Capture live face')
        : localStatus === 'Ready'
          ? t('pages.persons.scan.local.readyState', undefined, 'Ready to use')
          : t('pages.persons.scan.local.preview', undefined, 'Live preview');
  const previewGuideText =
    scanSource === 'system'
      ? localStatus === 'Step1Qr'
        ? t('pages.persons.scan.local.step1Hint', undefined, 'Present the QR code first.')
        : localStatus === 'Step2Face'
          ? t(
              'pages.persons.scan.local.step2Hint',
              undefined,
              'After QR is complete, keep the live face at the center of the preview.'
            )
          : faceReady
            ? t('pages.persons.scan.local.step2Done', undefined, 'Live face captured.')
            : t('pages.persons.scan.system.startHint', undefined, 'Choose Scan to start.')
      : !cameraEnabled
        ? t('pages.persons.scan.local.enableFirstHint', undefined, 'Enable the camera to start.')
        : localStatus === 'Step1Qr'
          ? t('pages.persons.scan.local.step1Hint', undefined, 'Present the QR code first.')
          : localStatus === 'Step2Face'
            ? t(
                'pages.persons.scan.local.step2Hint',
                undefined,
                'After QR is complete, keep the live face at the center of the preview.'
              )
            : faceReady
              ? t('pages.persons.scan.local.step2Done', undefined, 'Live face captured.')
              : t('pages.persons.scan.local.startHint', undefined, 'Choose Start to begin scanning.');

  function updateLocalStatus(next: LocalStatus) {
    localStatusRef.current = next;
    setLocalStatus(next);
  }

  async function refreshLocalDevices() {
    if (!navigator.mediaDevices?.enumerateDevices) {
      setLocalDevices([]);
      return;
    }

    const devices = await navigator.mediaDevices.enumerateDevices();
    const videos = devices
      .filter((device) => device.kind === 'videoinput')
      .map((device, index) => ({
        value: device.deviceId,
        label:
          device.label ||
          t('pages.persons.scan.local.unnamedCamera', { index: index + 1 }, `Camera ${index + 1}`)
      }));

    setLocalDevices(videos);
    setLocalDeviceId((current) => current || videos[0]?.value || '');
  }

  async function enableCamera() {
    if (!navigator.mediaDevices?.getUserMedia) {
      setLocalError(
        t(
          'pages.persons.scan.local.unsupported',
          undefined,
          'This browser does not support camera access.'
        )
      );
      return;
    }

    try {
      await stopCamera();
      setLocalError(null);

      const constraints: MediaStreamConstraints = {
        video: localDeviceId
          ? {
              deviceId: { ideal: localDeviceId },
              width: { ideal: 1280 },
              height: { ideal: 720 },
              facingMode: 'user'
            }
          : {
              width: { ideal: 1280 },
              height: { ideal: 720 },
              facingMode: 'user'
            },
        audio: false
      };

      const stream = await navigator.mediaDevices.getUserMedia(constraints);
      streamRef.current = stream;

      const video = videoRef.current;
      if (video) {
        video.srcObject = stream;
        await video.play();
      }

      setCameraEnabled(true);
      updateLocalStatus('Previewing');
      await refreshLocalDevices();
    } catch (error) {
      setLocalError(
        (error as Error)?.message ??
          t('pages.persons.scan.local.enableFailed', undefined, 'Unable to enable camera.')
      );
      setCameraEnabled(false);
    }
  }

  async function stopCamera() {
    stopLoops();

    const stream = streamRef.current;
    if (stream) {
      stream.getTracks().forEach((track) => track.stop());
      streamRef.current = null;
    }

    const video = videoRef.current;
    if (video) {
      video.pause();
      video.srcObject = null;
    }

    setCameraEnabled(false);
  }

  function resetModalState() {
    stopLoops();
    void stopCamera();
    setSystemResult(null);
    setSessionId(null);
    sessionIdRef.current = null;
    sessionPromiseRef.current = null;
    setParsedPerson(null);
    setRawQrPayload(null);
    setFaceImageBase64(null);
    setLocalError(null);
    setFaceHint(null);
    setLocalBusy(false);
    setSystemBusy(false);
    updateLocalStatus('Previewing');
  }

  function stopLoops() {
    if (scanTimerRef.current !== null) {
      window.clearTimeout(scanTimerRef.current);
      scanTimerRef.current = null;
    }

    localLoopVersionRef.current += 1;
    stopSystemLoop();
  }

  function stopSystemLoop() {
    if (systemTimerRef.current !== null) {
      window.clearTimeout(systemTimerRef.current);
      systemTimerRef.current = null;
    }

    systemLoopVersionRef.current += 1;
    systemLoopModeRef.current = 'idle';
  }

  function beginLocalFlow() {
    if (!cameraEnabled) {
      setLocalError(
        t('pages.persons.scan.local.enableFirst', undefined, 'Enable the camera first.')
      );
      return;
    }

    stopLoops();
    setParsedPerson(null);
    setRawQrPayload(null);
    setFaceImageBase64(null);
    setFaceHint(null);
    setLocalError(null);
    updateLocalStatus('Step1Qr');
    setLocalBusy(true);
    void runQrLoop();
  }

  function redoStep1() {
    stopLoops();
    setParsedPerson(null);
    setRawQrPayload(null);
    setLocalError(null);
    if (!cameraEnabled) {
      updateLocalStatus('Previewing');
      return;
    }

    updateLocalStatus('Step1Qr');
    setLocalBusy(true);
    void runQrLoop();
  }

  function redoStep2() {
    if (!parsedPerson || !cameraEnabled) {
      return;
    }

    stopLoops();
    setFaceImageBase64(null);
    setFaceHint(null);
    setLocalError(null);
    updateLocalStatus('Step2Face');
    setLocalBusy(true);
    void runFaceLoop();
  }
  async function runQrLoop() {
    const detectorWindow = window as DetectorWindow;
    const barcodeDetector = detectorWindow.BarcodeDetector
      ? new detectorWindow.BarcodeDetector({ formats: ['qr_code'] })
      : null;
    const token = localLoopVersionRef.current;

    const tick = async () => {
      if (localLoopVersionRef.current !== token || localStatusRef.current !== 'Step1Qr') {
        setLocalBusy(false);
        return;
      }

      const frame = captureCurrentFrame();
      if (!frame) {
        schedule(tick, 220, token);
        return;
      }

      try {
        const qrText = await detectQrFromFrame(frame.canvas, frame.context, barcodeDetector);
        if (localLoopVersionRef.current !== token || localStatusRef.current !== 'Step1Qr') {
          setLocalBusy(false);
          return;
        }

        if (qrText) {
          const person = parseVneidQrPayload(qrText);
          setParsedPerson(person);
          setRawQrPayload(qrText);
          if (faceImageRef.current) {
            updateLocalStatus('Ready');
            setLocalBusy(false);
          } else {
            updateLocalStatus('Step2Face');
            setLocalBusy(true);
            schedule(runFaceLoop, 120, token);
          }
          return;
        }
      } catch (error) {
        if (localLoopVersionRef.current !== token) {
          return;
        }

        setLocalError(
          (error as Error)?.message ??
            t('pages.persons.scan.errors.qrFailed', undefined, 'QR scan failed.')
        );
        updateLocalStatus('Error');
        setLocalBusy(false);
        return;
      }

      schedule(tick, 220, token);
    };

    await tick();
  }

  async function runFaceLoop() {
    const detectorWindow = window as DetectorWindow;
    const faceDetector = detectorWindow.FaceDetector
      ? new detectorWindow.FaceDetector({ fastMode: true, maxDetectedFaces: 3 })
      : null;
    const token = localLoopVersionRef.current;

    const tick = async () => {
      if (localLoopVersionRef.current !== token || localStatusRef.current !== 'Step2Face') {
        setLocalBusy(false);
        return;
      }

      const frame = captureCurrentFrame();
      if (!frame) {
        schedule(tick, 280, token);
        return;
      }

      try {
        let faceDataUrl: string | null = null;
        if (faceDetector) {
          faceDataUrl = await detectFaceFromNative(frame.canvas, faceDetector);
          setFaceHint(null);
        } else {
          faceDataUrl = detectCenterFaceFallback(frame.canvas);
          setFaceHint(
            faceDataUrl
              ? t(
                  'pages.persons.scan.local.faceFallback',
                  undefined,
                  'Native face detection is unavailable. Using a strict center capture fallback.'
                )
              : t(
                  'pages.persons.scan.local.faceUnavailable',
                  undefined,
                  'This browser cannot reliably capture a live face automatically. Keep your face centered and try again.'
                )
          );
        }

        if (localLoopVersionRef.current !== token || localStatusRef.current !== 'Step2Face') {
          setLocalBusy(false);
          return;
        }

        if (faceDataUrl) {
          setFaceImageBase64(faceDataUrl);
          updateLocalStatus('Ready');
          setLocalBusy(false);
          return;
        }
      } catch (error) {
        if (localLoopVersionRef.current !== token) {
          return;
        }

        setLocalError(
          (error as Error)?.message ??
            t('pages.persons.scan.errors.faceFailed', undefined, 'Face capture failed.')
        );
        updateLocalStatus('Error');
        setLocalBusy(false);
        return;
      }

      schedule(tick, 320, token);
    };

    await tick();
  }

  function schedule(callback: () => void | Promise<void>, delayMs: number, token: number) {
    if (localLoopVersionRef.current !== token) {
      return;
    }

    if (scanTimerRef.current !== null) {
      window.clearTimeout(scanTimerRef.current);
    }

    scanTimerRef.current = window.setTimeout(() => {
      if (localLoopVersionRef.current === token) {
        void callback();
      }
    }, delayMs);
  }

  function captureCurrentFrame() {
    const video = videoRef.current;
    if (!video || video.videoWidth === 0 || video.videoHeight === 0) {
      return null;
    }

    let canvas = canvasRef.current;
    if (!canvas) {
      canvas = document.createElement('canvas');
      canvasRef.current = canvas;
    }

    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    const context = canvas.getContext('2d', { willReadFrequently: true });
    if (!context) {
      return null;
    }

    context.drawImage(video, 0, 0, canvas.width, canvas.height);
    return { canvas, context };
  }
  async function detectQrFromFrame(
    canvas: HTMLCanvasElement,
    context: CanvasRenderingContext2D,
    barcodeDetector: BarcodeDetectorLike | null
  ) {
    if (barcodeDetector) {
      const results = await barcodeDetector.detect(canvas);
      const value = results.find((item) => item.rawValue?.trim())?.rawValue?.trim();
      if (value) {
        return value;
      }
    }

    const imageData = context.getImageData(0, 0, canvas.width, canvas.height);
    const result = jsQR(imageData.data, imageData.width, imageData.height, {
      inversionAttempts: 'dontInvert'
    });
    return result?.data?.trim() || null;
  }
  async function detectFaceFromNative(canvas: HTMLCanvasElement, faceDetector: FaceDetectorLike) {
    const faces = await faceDetector.detect(canvas);
    if (faces.length === 0) {
      return null;
    }

    const selected = pickCenteredFace(faces, canvas.width, canvas.height);
    if (!selected) {
      return null;
    }

    return cropFaceFromBox(canvas, selected.boundingBox);
  }
  function detectCenterFaceFallback(canvas: HTMLCanvasElement) {
    const cropWidth = Math.round(canvas.width * 0.34);
    const cropHeight = Math.round(cropWidth * 1.28);
    const cropX = Math.round((canvas.width - cropWidth) / 2);
    const cropY = Math.round(canvas.height * 0.14);
    const maxCropY = Math.max(0, canvas.height - cropHeight);
    const finalY = clamp(cropY, 0, maxCropY);

    const context = canvas.getContext('2d', { willReadFrequently: true });
    if (!context) {
      return null;
    }

    const imageData = context.getImageData(cropX, finalY, cropWidth, cropHeight);
    const stats = analyzePortraitLikelihood(imageData);
    if (stats.skinRatio < 0.14 || stats.centerSkinRatio < 0.18 || stats.lumaStdDev < 22) {
      return null;
    }

    if (stats.centerSkinRatio < stats.edgeSkinRatio + 0.04) {
      return null;
    }

    return exportCrop(canvas, cropX, finalY, cropWidth, cropHeight);
  }
  function pickCenteredFace(
    faces: DetectedFaceLike[],
    width: number,
    height: number
  ) {
    const centerX = width / 2;
    const centerY = height / 2;

    return faces
      .map((face) => {
        const box = face.boundingBox;
        const boxCenterX = box.x + box.width / 2;
        const boxCenterY = box.y + box.height / 2;
        const distance = Math.hypot(centerX - boxCenterX, centerY - boxCenterY);
        return { face, distance };
      })
      .filter(({ face }) => {
        const box = face.boundingBox;
        return (
          box.width >= width * 0.12 &&
          box.height >= height * 0.18 &&
          box.width <= width * 0.7 &&
          box.height <= height * 0.8
        );
      })
      .sort((left, right) => left.distance - right.distance)[0]?.face;
  }

  function cropFaceFromBox(canvas: HTMLCanvasElement, box: DOMRectReadOnly) {
    const x = clamp(box.x - box.width * 0.34, 0, canvas.width);
    const y = clamp(box.y - box.height * 0.45, 0, canvas.height);
    const right = clamp(box.x + box.width * 1.34, 0, canvas.width);
    const bottom = clamp(box.y + box.height * 1.55, 0, canvas.height);

    if (right - x < 64 || bottom - y < 80) {
      return null;
    }

    return exportCrop(canvas, x, y, right - x, bottom - y);
  }

  function analyzePortraitLikelihood(imageData: ImageData) {
    const { data, width, height } = imageData;
    let pixelCount = 0;
    let skinCount = 0;
    let centerSkinCount = 0;
    let edgeSkinCount = 0;
    let lumaSum = 0;
    let lumaSquaredSum = 0;

    for (let y = 0; y < height; y += 2) {
      for (let x = 0; x < width; x += 2) {
        const index = (y * width + x) * 4;
        const red = data[index];
        const green = data[index + 1];
        const blue = data[index + 2];
        const luma = 0.299 * red + 0.587 * green + 0.114 * blue;
        const cb = 128 - 0.168736 * red - 0.331264 * green + 0.5 * blue;
        const cr = 128 + 0.5 * red - 0.418688 * green - 0.081312 * blue;
        const isSkin =
          cb >= 77 &&
          cb <= 127 &&
          cr >= 133 &&
          cr <= 173 &&
          red > 90 &&
          green > 40 &&
          blue > 20;

        pixelCount += 1;
        lumaSum += luma;
        lumaSquaredSum += luma * luma;
        if (!isSkin) {
          continue;
        }

        skinCount += 1;
        const isCenter =
          x > width * 0.2 && x < width * 0.8 && y > height * 0.15 && y < height * 0.85;
        if (isCenter) {
          centerSkinCount += 1;
        } else {
          edgeSkinCount += 1;
        }
      }
    }

    const skinRatio = pixelCount === 0 ? 0 : skinCount / pixelCount;
    const centerPixelCount = Math.max(1, Math.floor(pixelCount * 0.36));
    const edgePixelCount = Math.max(1, pixelCount - centerPixelCount);
    const centerSkinRatio = centerSkinCount / centerPixelCount;
    const edgeSkinRatio = edgeSkinCount / edgePixelCount;
    const mean = pixelCount === 0 ? 0 : lumaSum / pixelCount;
    const variance = pixelCount === 0 ? 0 : lumaSquaredSum / pixelCount - mean * mean;

    return {
      skinRatio,
      centerSkinRatio,
      edgeSkinRatio,
      lumaStdDev: Math.sqrt(Math.max(0, variance))
    };
  }

  function exportCrop(
    canvas: HTMLCanvasElement,
    x: number,
    y: number,
    width: number,
    height: number
  ) {
    const exportCanvas = document.createElement('canvas');
    exportCanvas.width = Math.max(1, Math.round(width));
    exportCanvas.height = Math.max(1, Math.round(height));
    const exportContext = exportCanvas.getContext('2d');
    if (!exportContext) {
      return null;
    }

    exportContext.drawImage(
      canvas,
      x,
      y,
      width,
      height,
      0,
      0,
      exportCanvas.width,
      exportCanvas.height
    );

    return exportCanvas.toDataURL('image/jpeg', 0.92);
  }

  function clamp(value: number, min: number, max: number) {
    return Math.min(Math.max(value, min), max);
  }

  function scheduleSystem(callback: () => void | Promise<void>, delayMs: number, token: number) {
    if (systemTimerRef.current !== null) {
      window.clearTimeout(systemTimerRef.current);
    }

    systemTimerRef.current = window.setTimeout(() => {
      if (systemLoopVersionRef.current === token) {
        void callback();
      }
    }, delayMs);
  }

  async function ensureSystemSession() {
    if (!systemCameraId) {
      return null;
    }

    if (sessionIdRef.current) {
      return sessionIdRef.current;
    }

    if (!sessionPromiseRef.current) {
      sessionPromiseRef.current = createPersonScanSession({ cameraId: systemCameraId })
        .then((created) => {
          sessionIdRef.current = created.sessionId;
          setSessionId(created.sessionId);
          return created.sessionId;
        })
        .finally(() => {
          sessionPromiseRef.current = null;
        });
    }

    return sessionPromiseRef.current;
  }

  function applySystemScanResult(
    result: PersonScanResult,
    options: { preserveFace?: boolean } = {}
  ) {
    const fallbackPerson =
      result.person ?? (result.rawQrPayload ? parseVneidQrPayload(result.rawQrPayload) : null);
    const normalizedResult = {
      ...result,
      person: fallbackPerson
    };

    setSystemResult(normalizedResult);
    setParsedPerson(fallbackPerson);
    setRawQrPayload(normalizedResult.rawQrPayload ?? fallbackPerson?.rawQrPayload ?? null);
    setFaceImageBase64(
      options.preserveFace
        ? normalizedResult.faceImageBase64 ?? faceImageRef.current ?? null
        : normalizedResult.faceImageBase64 ?? null
    );
    setLocalError(normalizedResult.errorMessage ?? null);

    return normalizedResult;
  }

  async function requestSystemScan(
    mode: 'preview' | 'qr' | 'face',
    options: { resetQr?: boolean; resetFace?: boolean } = {}
  ) {
    const nextSessionId = await ensureSystemSession();
    if (!nextSessionId) {
      throw new Error(t('pages.persons.validation.cameraRequired'));
    }

    return scanPersonScanSession(nextSessionId, {
      mode,
      resetQr: options.resetQr ?? false,
      resetFace: options.resetFace ?? false
    });
  }

  function beginSystemFlow() {
    if (!systemCameraId) {
      setLocalError(t('pages.persons.validation.cameraRequired'));
      return;
    }

    stopLoops();
    setParsedPerson(null);
    setRawQrPayload(null);
    setFaceImageBase64(null);
    setFaceHint(null);
    setLocalError(null);
    updateLocalStatus('Step1Qr');
    setSystemBusy(true);
    void runSystemQrLoop(true);
  }

  async function runSystemQrLoop(resetValues: boolean) {
    stopSystemLoop();
    systemLoopModeRef.current = 'step1';
    const token = systemLoopVersionRef.current;
    let shouldReset = resetValues;

    const tick = async () => {
      if (systemLoopVersionRef.current !== token || systemLoopModeRef.current !== 'step1') {
        return;
      }

      try {
        const result = await requestSystemScan('qr', {
          resetQr: shouldReset,
          resetFace: shouldReset
        });
        shouldReset = false;

        if (systemLoopVersionRef.current !== token || systemLoopModeRef.current !== 'step1') {
          return;
        }

        const normalizedResult = applySystemScanResult(result, {
          preserveFace: Boolean(faceImageRef.current)
        });

        if (normalizedResult.qrDetected) {
          if (faceImageRef.current) {
            updateLocalStatus('Ready');
            setSystemBusy(false);
          } else {
            updateLocalStatus('Step2Face');
            setSystemBusy(true);
            void runSystemFaceLoop(false);
          }
          return;
        }
      } catch (error) {
        if (systemLoopVersionRef.current !== token) {
          return;
        }

        setLocalError(
          (error as Error)?.message ??
            t('pages.persons.scan.errors.systemFailed', undefined, 'System camera scan failed.')
        );
        updateLocalStatus('Error');
        setSystemBusy(false);
        return;
      }

      scheduleSystem(tick, 900, token);
    };

    await tick();
  }

  async function runSystemFaceLoop(resetFace: boolean) {
    stopSystemLoop();
    systemLoopModeRef.current = 'step2';
    const token = systemLoopVersionRef.current;
    let shouldReset = resetFace;

    const tick = async () => {
      if (systemLoopVersionRef.current !== token || systemLoopModeRef.current !== 'step2') {
        return;
      }

      try {
        const result = await requestSystemScan('face', {
          resetFace: shouldReset
        });
        shouldReset = false;

        if (systemLoopVersionRef.current !== token || systemLoopModeRef.current !== 'step2') {
          return;
        }

        const normalizedResult = applySystemScanResult(result);

        if (normalizedResult.faceDetected) {
          updateLocalStatus('Ready');
          setSystemBusy(false);
          return;
        }
      } catch (error) {
        if (systemLoopVersionRef.current !== token) {
          return;
        }

        setLocalError(
          (error as Error)?.message ??
            t('pages.persons.scan.errors.systemFailed', undefined, 'System camera scan failed.')
        );
        updateLocalStatus('Error');
        setSystemBusy(false);
        return;
      }

      scheduleSystem(tick, 900, token);
    };

    await tick();
  }

  function redoSystemStep1() {
    if (!systemCameraId) {
      return;
    }

    stopLoops();
    setSystemResult(null);
    setParsedPerson(null);
    setRawQrPayload(null);
    setLocalError(null);
    updateLocalStatus('Step1Qr');
    setSystemBusy(true);
    void runSystemQrLoop(true);
  }

  function redoSystemStep2() {
    if (!systemCameraId || !activeRawQrPayload) {
      return;
    }

    stopLoops();
    setFaceImageBase64(null);
    setFaceHint(null);
    setLocalError(null);
    updateLocalStatus('Step2Face');
    setSystemBusy(true);
    void runSystemFaceLoop(true);
  }

  async function handleUseInForm() {
    if (!activePerson || !faceImageBase64) {
      return;
    }

    await onUseInForm({
      person: activePerson,
      faceImageBase64,
      source: scanSource,
      systemCameraId: scanSource === 'system' ? systemCameraId : null,
      rawQrPayload: activeRawQrPayload
    });

    resetModalState();
    onClose();
  }

  return (
    <Modal
      opened={opened}
      onClose={() => {
        resetModalState();
        onClose();
      }}
      title={t('pages.persons.scan.title', undefined, 'Scan VNeID')}
      size="78rem"
    >
      <Stack gap="md">
        <Radio.Group
          label={t('pages.persons.scan.sourceLabel', undefined, 'Scan source')}
          value={scanSource}
          onChange={(value) => setScanSource((value as ScanSource) || 'system')}
        >
          <Group mt="xs" grow align="stretch">
            {scanSourceOptions.map((option) => {
              const active = option.value === scanSource;
              return (
                <Paper
                  key={option.value}
                  p="sm"
                  radius="lg"
                  withBorder
                  onClick={() => setScanSource(option.value as ScanSource)}
                  style={{
                    cursor: 'pointer',
                    borderColor: active ? 'var(--mantine-color-orange-6)' : undefined,
                    background: active ? 'rgba(255, 122, 0, 0.08)' : undefined
                  }}
                >
                  <Radio value={option.value} label={option.label} />
                </Paper>
              );
            })}
          </Group>
        </Radio.Group>

        {scanSource === 'system' ? (
          <Stack gap="md">
            <Paper p="md" radius="xl" className="surface-card">
              <Stack gap="md">
                <Group align="flex-end" wrap="wrap">
                  <Select
                    style={{ flex: '1 1 22rem' }}
                    label={t('pages.persons.scan.system.camera', undefined, 'System camera')}
                    data={cameraOptions}
                    value={systemCameraId}
                    onChange={(value) => {
                      setSystemCameraId(value ?? '');
                      setSessionId(null);
                      sessionIdRef.current = null;
                      sessionPromiseRef.current = null;
                      setSystemResult(null);
                      setParsedPerson(null);
                      setRawQrPayload(null);
                      setFaceImageBase64(null);
                    }}
                    searchable
                    nothingFoundMessage={t('pages.persons.scan.system.noCamera', undefined, 'No configured system cameras.')}
                  />
                  <Button
                    leftSection={<IconPlayerPlay size={16} />}
                    onClick={beginSystemFlow}
                    loading={systemBusy}
                    disabled={!systemCameraId}
                  >
                    {t('pages.persons.scan.system.scanNow', undefined, 'Scan now')}
                  </Button>
                  {systemResult && (
                    <Badge
                      color={systemResult.faceDetected && systemResult.qrDetected ? 'brand' : 'yellow'}
                      variant="light"
                    >
                      {systemResult.status}
                    </Badge>
                  )}
                </Group>

                <Group align="stretch" wrap="wrap" gap="md">
                  <Paper
                    p="md"
                    radius="xl"
                    style={{
                      flex: '1 1 42rem',
                      minWidth: 0,
                      background:
                        'radial-gradient(circle at top left, rgba(255, 122, 0, 0.18), transparent 34%), rgba(7, 10, 16, 0.98)',
                      border: '1px solid rgba(255, 255, 255, 0.06)'
                    }}
                  >
                    <Stack gap="sm">
                      <Group justify="space-between" align="center">
                        <Text size="sm" fw={600} c="white">
                          {t('pages.persons.scan.local.preview', undefined, 'Live preview')}
                        </Text>
                        <Group gap="xs">
                          <Badge color={previewStatusColor} variant="light">
                            {previewStatusLabel}
                          </Badge>
                          <Badge color={faceReady ? 'green' : qrReady ? 'yellow' : 'gray'} variant="light">
                            {stepLabel}
                          </Badge>
                        </Group>
                      </Group>
                      <Box
                        style={{
                          position: 'relative',
                          borderRadius: 18,
                          overflow: 'hidden',
                          minHeight: 430,
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          background:
                            'linear-gradient(135deg, rgba(255,255,255,0.04), rgba(255,255,255,0.02))'
                        }}
                      >
                        {systemResult?.snapshotImageBase64 ? (
                          <img
                            src={systemResult.snapshotImageBase64}
                            alt={t('pages.persons.scan.local.preview', undefined, 'Live preview')}
                            style={{
                              width: '100%',
                              height: '100%',
                              minHeight: 430,
                              objectFit: 'contain'
                            }}
                          />
                        ) : (
                          <Stack
                            gap="xs"
                            align="center"
                            style={{
                              position: 'absolute',
                              inset: 0,
                              justifyContent: 'center',
                              pointerEvents: 'none'
                            }}
                          >
                            {systemBusy ? (
                              <Loader size="md" color="orange" />
                            ) : (
                              <IconCamera size={34} color="rgba(255,255,255,0.74)" />
                            )}
                          </Stack>
                        )}
                        <Box
                          style={{
                            position: 'absolute',
                            inset: 0,
                            background:
                              'linear-gradient(180deg, rgba(0,0,0,0.12) 0%, rgba(0,0,0,0.02) 24%, rgba(0,0,0,0.28) 100%)',
                            pointerEvents: 'none'
                          }}
                        />
                        <Box
                          style={{
                            position: 'absolute',
                            left: '50%',
                            top: '50%',
                            transform: 'translate(-50%, -50%)',
                            width: '32%',
                            minWidth: 140,
                            maxWidth: 240,
                            aspectRatio: '0.78',
                            borderRadius: 20,
                            border: '2px dashed rgba(255,255,255,0.82)',
                            boxShadow: '0 0 0 999px rgba(0, 0, 0, 0.14)',
                            pointerEvents: 'none'
                          }}
                        />
                        <Box
                          style={{
                            position: 'absolute',
                            left: 16,
                            right: 16,
                            bottom: 16,
                            borderRadius: 14,
                            padding: '12px 14px',
                            background: 'rgba(8, 10, 14, 0.68)',
                            border: '1px solid rgba(255,255,255,0.08)',
                            backdropFilter: 'blur(10px)',
                            pointerEvents: 'none'
                          }}
                        >
                          <Text size="sm" fw={600} c="white">
                            {previewGuideTitle}
                          </Text>
                          <Text size="sm" c="rgba(255,255,255,0.72)">
                            {previewGuideText}
                          </Text>
                        </Box>
                      </Box>
                    </Stack>
                  </Paper>

                  <Paper
                    p="md"
                    radius="xl"
                    className="surface-card"
                    style={{ flex: '0 1 20rem', minWidth: 280 }}
                  >
                    <Stack gap="sm">
                      <ScanStepCard
                        icon={<IconQrcode size={16} />}
                        title={t('pages.persons.scan.local.step1Title', undefined, 'Step 1 - Read QR')}
                        description={
                          activeRawQrPayload
                            ? t('pages.persons.scan.local.step1Done', undefined, 'QR detected. Person data is ready.')
                            : t('pages.persons.scan.local.step1Hint', undefined, 'Present the QR code first.')
                        }
                        badgeLabel={
                          activeRawQrPayload
                            ? t('pages.persons.scan.local.step1Complete', undefined, 'Completed')
                            : t('pages.persons.scan.local.step1Title', undefined, 'Step 1 - Read QR')
                        }
                        completed={Boolean(activeRawQrPayload)}
                        onRedo={redoSystemStep1}
                        redoDisabled={!systemCameraId || systemBusy}
                        t={t}
                      />

                      <ScanStepCard
                        icon={<IconCamera size={16} />}
                        title={t('pages.persons.scan.local.step2Title', undefined, 'Step 2 - Capture live face')}
                        description={
                          faceImageBase64
                            ? t('pages.persons.scan.local.step2Done', undefined, 'Live face captured.')
                            : t(
                                'pages.persons.scan.local.step2Hint',
                                undefined,
                                'After QR is complete, keep the live face at the center of the preview.'
                              )
                        }
                        badgeLabel={
                          faceImageBase64
                            ? t('pages.persons.scan.local.step2Complete', undefined, 'Completed')
                            : t('pages.persons.scan.local.step2Title', undefined, 'Step 2 - Capture live face')
                        }
                        completed={Boolean(faceImageBase64)}
                        onRedo={redoSystemStep2}
                        redoDisabled={!systemCameraId || systemBusy}
                        t={t}
                      />
                    </Stack>
                  </Paper>
                </Group>
              </Stack>
            </Paper>
          </Stack>
        ) : (
          <Stack gap="md">
            <Paper p="md" radius="xl" className="surface-card">
              <Stack gap="md">
                <Group align="flex-end" wrap="wrap">
                  <Select
                    style={{ flex: '1 1 22rem' }}
                    label={t('pages.persons.scan.local.camera', undefined, 'Internal / USB camera')}
                    data={localDevices}
                    value={localDeviceId}
                    onChange={(value) => setLocalDeviceId(value ?? '')}
                    searchable
                    nothingFoundMessage={t('pages.persons.scan.local.noDevice', undefined, 'No local camera devices found.')}
                  />
                  <Group gap="sm">
                    <Button variant="light" leftSection={<IconCamera size={16} />} onClick={() => void enableCamera()}>
                      {t('pages.persons.scan.local.enableCamera', undefined, 'Enable camera')}
                    </Button>
                    <Button
                      variant="subtle"
                      color="red"
                      leftSection={<IconPlayerStop size={16} />}
                      onClick={() => void stopCamera()}
                    >
                      {t('pages.persons.scan.local.stopCamera', undefined, 'Stop camera')}
                    </Button>
                    <Button
                      leftSection={<IconPlayerPlay size={16} />}
                      onClick={beginLocalFlow}
                      disabled={!cameraEnabled}
                      loading={localBusy && localStatus === 'Step1Qr'}
                    >
                      {t('pages.persons.scan.local.start', undefined, 'Start')}
                    </Button>
                  </Group>
                </Group>

                <Group align="stretch" wrap="wrap" gap="md">
                  <Paper
                    p="md"
                    radius="xl"
                    style={{
                      flex: '1 1 42rem',
                      minWidth: 0,
                      background:
                        'radial-gradient(circle at top left, rgba(255, 122, 0, 0.18), transparent 34%), rgba(7, 10, 16, 0.98)',
                      border: '1px solid rgba(255, 255, 255, 0.06)'
                    }}
                  >
                    <Stack gap="sm">
                      <Group justify="space-between" align="center">
                        <Text size="sm" fw={600} c="white">
                          {t('pages.persons.scan.local.preview', undefined, 'Live preview')}
                        </Text>
                        <Group gap="xs">
                          <Badge color={previewStatusColor} variant="light">
                            {previewStatusLabel}
                          </Badge>
                          <Badge color={faceReady ? 'green' : qrReady ? 'yellow' : 'gray'} variant="light">
                            {stepLabel}
                          </Badge>
                        </Group>
                      </Group>
                      <Box
                        style={{
                          position: 'relative',
                          borderRadius: 18,
                          overflow: 'hidden',
                          minHeight: 430,
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          background:
                            'linear-gradient(135deg, rgba(255,255,255,0.04), rgba(255,255,255,0.02))'
                        }}
                      >
                        <video
                          ref={videoRef}
                          muted
                          playsInline
                          autoPlay
                          style={{
                            width: '100%',
                            height: '100%',
                            minHeight: 430,
                            objectFit: 'contain',
                            opacity: cameraEnabled ? 1 : 0.18
                          }}
                        />
                        {!cameraEnabled && (
                          <Stack
                            gap="xs"
                            align="center"
                            style={{
                              position: 'absolute',
                              inset: 0,
                              justifyContent: 'center',
                              pointerEvents: 'none'
                            }}
                          >
                            <IconCamera size={34} color="rgba(255,255,255,0.74)" />
                          </Stack>
                        )}
                        <Box
                          style={{
                            position: 'absolute',
                            inset: 0,
                            background:
                              'linear-gradient(180deg, rgba(0,0,0,0.12) 0%, rgba(0,0,0,0.02) 24%, rgba(0,0,0,0.28) 100%)',
                            pointerEvents: 'none'
                          }}
                        />
                        <Box
                          style={{
                            position: 'absolute',
                            left: '50%',
                            top: '50%',
                            transform: 'translate(-50%, -50%)',
                            width: '32%',
                            minWidth: 140,
                            maxWidth: 240,
                            aspectRatio: '0.78',
                            borderRadius: 20,
                            border: '2px dashed rgba(255,255,255,0.82)',
                            boxShadow: '0 0 0 999px rgba(0, 0, 0, 0.14)',
                            pointerEvents: 'none'
                          }}
                        />
                        <Box
                          style={{
                            position: 'absolute',
                            left: 16,
                            right: 16,
                            bottom: 16,
                            borderRadius: 14,
                            padding: '12px 14px',
                            background: 'rgba(8, 10, 14, 0.68)',
                            border: '1px solid rgba(255,255,255,0.08)',
                            backdropFilter: 'blur(10px)',
                            pointerEvents: 'none'
                          }}
                        >
                          <Text size="sm" fw={600} c="white">
                            {previewGuideTitle}
                          </Text>
                          <Text size="sm" c="rgba(255,255,255,0.72)">
                            {previewGuideText}
                          </Text>
                        </Box>
                        {localBusy && (
                          <Loader
                            size="md"
                            color="orange"
                            style={{
                              position: 'absolute',
                              top: 18,
                              left: 18
                            }}
                          />
                        )}
                      </Box>
                    </Stack>
                  </Paper>

                  <Paper
                    p="md"
                    radius="xl"
                    className="surface-card"
                    style={{ flex: '0 1 20rem', minWidth: 280 }}
                  >
                    <Stack gap="sm">
                      <ScanStepCard
                        icon={<IconQrcode size={16} />}
                        title={t('pages.persons.scan.local.step1Title', undefined, 'Step 1 - Read QR')}
                        description={
                          rawQrPayload
                            ? t('pages.persons.scan.local.step1Done', undefined, 'QR detected. Person data is ready.')
                            : t('pages.persons.scan.local.step1Hint', undefined, 'Present the QR code first.')
                        }
                        badgeLabel={
                          rawQrPayload
                            ? t('pages.persons.scan.local.step1Complete', undefined, 'Completed')
                            : t('pages.persons.scan.local.step1Title', undefined, 'Step 1 - Read QR')
                        }
                        completed={Boolean(rawQrPayload)}
                        onRedo={redoStep1}
                        redoDisabled={!cameraEnabled}
                        t={t}
                      />

                      <ScanStepCard
                        icon={<IconCamera size={16} />}
                        title={t('pages.persons.scan.local.step2Title', undefined, 'Step 2 - Capture live face')}
                        description={
                          faceImageBase64
                            ? t('pages.persons.scan.local.step2Done', undefined, 'Live face captured.')
                            : t(
                                'pages.persons.scan.local.step2Hint',
                                undefined,
                                'After QR is complete, keep the live face at the center of the preview.'
                              )
                        }
                        badgeLabel={
                          faceImageBase64
                            ? t('pages.persons.scan.local.step2Complete', undefined, 'Completed')
                            : t('pages.persons.scan.local.step2Title', undefined, 'Step 2 - Capture live face')
                        }
                        completed={Boolean(faceImageBase64)}
                        onRedo={redoStep2}
                        redoDisabled={!parsedPerson}
                        t={t}
                      />
                    </Stack>
                  </Paper>
                </Group>
              </Stack>
            </Paper>
          </Stack>
        )}

        {(localError || faceHint) && (
          <Alert color={localError ? 'red' : 'blue'} icon={<IconAlertCircle size={16} />} variant="light">
            {localError ?? faceHint}
          </Alert>
        )}

        <Divider />

        <Group align="flex-start" wrap="wrap" gap="md">
          <Paper
            p="md"
            radius="xl"
            className="surface-card"
            style={{ flex: '1 1 38rem', minWidth: 0 }}
          >
            <Stack gap="sm">
              <Group justify="space-between" align="center">
                <Text fw={600}>
                  {t('pages.persons.scan.personPanel', undefined, 'Parsed person data')}
                </Text>
                {(localBusy || systemBusy) && <Loader size="sm" />}
              </Group>
              <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm">
                <Detail label={t('pages.persons.scan.fields.code', undefined, 'Code')} value={activePerson?.code} />
                <Detail label={t('pages.persons.scan.fields.personalId', undefined, 'Personal ID')} value={activePerson?.personalId} />
                <Detail label={t('pages.persons.scan.fields.documentNumber', undefined, 'Document number')} value={activePerson?.documentNumber} />
                <Detail label={t('pages.persons.scan.fields.fullName', undefined, 'Full name')} value={activePerson?.fullName ?? `${activePerson?.firstName ?? ''} ${activePerson?.lastName ?? ''}`.trim()} />
                <Detail label={t('pages.persons.scan.fields.gender', undefined, 'Gender')} value={activePerson?.gender} />
                <Detail label={t('pages.persons.scan.fields.dateOfBirth', undefined, 'Date of birth')} value={activePerson?.dateOfBirth} />
                <Detail label={t('pages.persons.scan.fields.dateOfIssue', undefined, 'Date of issue')} value={activePerson?.dateOfIssue} />
                <Detail label={t('pages.persons.scan.fields.age', undefined, 'Age')} value={activePerson?.age?.toString()} />
              </SimpleGrid>
              <Detail label={t('pages.persons.scan.fields.address', undefined, 'Address')} value={activePerson?.address} multiline />
              <Textarea
                label={t('pages.persons.scan.fields.rawQrPayload', undefined, 'Raw QR payload')}
                value={activeRawQrPayload ?? ''}
                autosize
                minRows={3}
                readOnly
              />
            </Stack>
          </Paper>

          <Paper
            p="md"
            radius="xl"
            className="surface-card"
            style={{ flex: '0 0 18rem', width: '18rem', maxWidth: '100%' }}
          >
            <Stack gap="sm">
              <Text fw={600}>
                {t('pages.persons.scan.facePanel', undefined, 'Detected face')}
              </Text>
              <Box
                style={{
                  width: '100%',
                  maxWidth: 220,
                  height: 180,
                  borderRadius: 18,
                  overflow: 'hidden',
                  border: '1px solid rgba(0,0,0,0.08)',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  background: 'rgba(0,0,0,0.05)',
                  alignSelf: 'center'
                }}
              >
                {faceImageBase64 ? (
                  <img
                    src={faceImageBase64}
                    alt={t('pages.persons.scan.faceAlt', undefined, 'Detected face')}
                    style={{ width: '100%', height: '100%', objectFit: 'contain' }}
                  />
                ) : (
                  <Text size="sm" className="muted-text">
                    {t('pages.persons.scan.faceEmpty', undefined, 'No face captured yet.')}
                  </Text>
                )}
              </Box>
              <Text size="sm" className="muted-text">
                {faceImageBase64
                  ? t('pages.persons.scan.local.step2Done', undefined, 'Live face captured.')
                  : t('pages.persons.scan.faceEmpty', undefined, 'No face captured yet.')}
              </Text>
              <Button
                fullWidth
                leftSection={<IconCamera size={16} />}
                onClick={() => void handleUseInForm()}
                disabled={!canUseInForm}
              >
                {t('pages.persons.scan.useInForm', undefined, 'Use in form')}
              </Button>
            </Stack>
          </Paper>
        </Group>
      </Stack>
    </Modal>
  );
}

function ScanStepCard({
  icon,
  title,
  description,
  badgeLabel,
  completed,
  onRedo,
  redoDisabled,
  t
}: {
  icon: ReactNode;
  title: string;
  description: string;
  badgeLabel: string;
  completed: boolean;
  onRedo: () => void;
  redoDisabled?: boolean;
  t: Translate;
}) {
  return (
    <Paper p="sm" radius="lg" withBorder>
      <Stack gap="xs">
        <Group justify="space-between" align="center">
          <Group gap="xs" align="center">
            {icon}
            <Text fw={700}>{title}</Text>
          </Group>
          <Button
            size="xs"
            variant="subtle"
            color="gray"
            leftSection={<IconRefresh size={14} />}
            onClick={onRedo}
            disabled={redoDisabled}
          >
            {t('pages.persons.scan.redo', undefined, 'Redo')}
          </Button>
        </Group>
        <Text size="sm" className="muted-text">
          {description}
        </Text>
        <Badge color={completed ? 'brand' : 'gray'} variant="light" w="fit-content">
          {badgeLabel}
        </Badge>
      </Stack>
    </Paper>
  );
}

function Detail({
  label,
  value,
  multiline = false
}: {
  label: string;
  value?: string | null;
  multiline?: boolean;
}) {
  return (
    <Stack gap={2}>
      <Text size="xs" className="muted-text">
        {label}
      </Text>
      <Text size="sm" style={multiline ? { whiteSpace: 'pre-wrap' } : undefined}>
        {value?.trim() || '-'}
      </Text>
    </Stack>
  );
}
