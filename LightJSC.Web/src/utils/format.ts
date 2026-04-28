type TranslateFn = (key: string, params?: Record<string, string | number>, fallback?: string) => string;

export function formatDateTime(value?: string | null, t?: TranslateFn) {
  if (!value) {
    return '-';
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  const now = new Date();
  const isSameDay =
    date.getFullYear() === now.getFullYear() &&
    date.getMonth() === now.getMonth() &&
    date.getDate() === now.getDate();

  if (isSameDay) {
    const diffMs = Math.max(0, now.getTime() - date.getTime());
    const seconds = Math.floor(diffMs / 1000);
    if (seconds < 60) {
      return (
        t?.('common.time.secondsAgo', { n: seconds }, `${seconds} giây trước`) ??
        `${seconds} giây trước`
      );
    }
    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) {
      return (
        t?.('common.time.minutesAgo', { n: minutes }, `${minutes} phút trước`) ??
        `${minutes} phút trước`
      );
    }
    const hours = Math.floor(minutes / 60);
    return (
      t?.('common.time.hoursAgo', { n: hours }, `${hours} giờ trước`) ??
      `${hours} giờ trước`
    );
  }

  const pad = (input: number) => String(input).padStart(2, '0');
  const hours = pad(date.getHours());
  const minutes = pad(date.getMinutes());
  const seconds = pad(date.getSeconds());
  const day = pad(date.getDate());
  const month = pad(date.getMonth() + 1);
  const year = date.getFullYear();

  return `${hours}:${minutes}:${seconds} ${day}/${month}/${year}`;
}

