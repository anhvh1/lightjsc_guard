import { ActionIcon, Tooltip } from '@mantine/core';
import { useI18n } from '../i18n/I18nProvider';

export function LanguageToggle() {
  const { language, setLanguage, t } = useI18n();
  const next = language === 'vi' ? 'en' : 'vi';
  const currentFlag = language === 'vi' ? '🇻🇳' : '🇺🇸';
  const tooltip = t(
    `common.language.switchTo.${next}`,
    undefined,
    next === 'vi' ? 'Chuyển sang tiếng Việt' : 'Switch to English'
  );

  return (
    <Tooltip label={tooltip}>
      <ActionIcon
        variant="light"
        color="brand"
        size="lg"
        onClick={() => setLanguage(next)}
        aria-label={tooltip}
      >
        <span aria-hidden="true" style={{ fontSize: 16, lineHeight: 1 }}>
          {currentFlag}
        </span>
      </ActionIcon>
    </Tooltip>
  );
}
