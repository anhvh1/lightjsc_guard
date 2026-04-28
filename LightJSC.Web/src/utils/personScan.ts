import type { PersonScanPerson } from '../api/types';

export function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      if (typeof reader.result === 'string') {
        resolve(reader.result);
        return;
      }
      reject(new Error('Unable to read file.'));
    };
    reader.onerror = () => reject(reader.error ?? new Error('Unable to read file.'));
    reader.readAsDataURL(file);
  });
}

export function dataUrlToFile(dataUrl: string, fileName = 'scan-face.jpg'): File {
  const [header, body] = dataUrl.split(',', 2);
  if (!header || !body) {
    throw new Error('Invalid image data.');
  }

  const mimeMatch = header.match(/data:(.*?);base64/i);
  const mimeType = mimeMatch?.[1] ?? 'image/jpeg';
  const binary = atob(body);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }

  return new File([bytes], fileName, { type: mimeType });
}

export function parseVneidQrPayload(rawPayload: string): PersonScanPerson | null {
  const payload = rawPayload.trim();
  if (!payload) {
    return null;
  }

  const fields = payload.split('|');
  const personalId = normalize(fields[0]);
  const documentNumber = normalize(fields[1]);
  const fullName = normalize(fields[2]);
  const dateOfBirth = parseCompactDate(normalize(fields[3]));
  const gender = normalize(fields[4]);
  const address = normalize(fields[5]);
  const dateOfIssue = parseCompactDate(normalize(fields[6]));
  const { firstName, lastName } = splitFullName(fullName);

  return {
    code: documentNumber ?? personalId ?? null,
    personalId,
    documentNumber,
    fullName,
    firstName,
    lastName,
    gender,
    dateOfBirth,
    dateOfIssue,
    age: calculateAge(dateOfBirth),
    address,
    rawQrPayload: payload
  };
}

export function splitFullName(fullName?: string | null) {
  const normalized = normalize(fullName);
  if (!normalized) {
    return { firstName: '', lastName: '' };
  }

  const tokens = normalized.split(/\s+/).filter(Boolean);
  if (tokens.length === 1) {
    return { firstName: tokens[0], lastName: '' };
  }

  return {
    firstName: tokens.slice(0, -1).join(' '),
    lastName: tokens[tokens.length - 1]
  };
}

function normalize(value?: string | null) {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

function parseCompactDate(value?: string | null) {
  if (!value || value.length !== 8) {
    return null;
  }

  const day = Number(value.slice(0, 2));
  const month = Number(value.slice(2, 4));
  const year = Number(value.slice(4, 8));
  if (!day || !month || !year) {
    return null;
  }

  return `${year.toString().padStart(4, '0')}-${month
    .toString()
    .padStart(2, '0')}-${day.toString().padStart(2, '0')}`;
}

function calculateAge(dateValue?: string | null) {
  if (!dateValue) {
    return null;
  }

  const parsed = new Date(`${dateValue}T00:00:00`);
  if (Number.isNaN(parsed.getTime())) {
    return null;
  }

  const today = new Date();
  let age = today.getFullYear() - parsed.getFullYear();
  const monthDelta = today.getMonth() - parsed.getMonth();
  if (monthDelta < 0 || (monthDelta === 0 && today.getDate() < parsed.getDate())) {
    age -= 1;
  }

  return age >= 0 ? age : null;
}
