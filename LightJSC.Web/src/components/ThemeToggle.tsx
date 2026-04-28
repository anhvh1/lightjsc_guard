import { ActionIcon, Tooltip, useMantineColorScheme } from '@mantine/core';
import { IconMoon, IconSun } from '@tabler/icons-react';
import { useI18n } from '../i18n/I18nProvider';

export function ThemeToggle() {
  const { colorScheme, setColorScheme } = useMantineColorScheme();
  const isDark = colorScheme === 'dark';
  const { t } = useI18n();
  const tooltip = isDark
    ? t('common.theme.switchToLight', undefined, 'Switch to light')
    : t('common.theme.switchToDark', undefined, 'Switch to dark');

  return (
    <Tooltip label={tooltip}>
      <ActionIcon
        variant="light"
        color={isDark ? 'amber' : 'brand'}
        size="lg"
        onClick={() => setColorScheme(isDark ? 'light' : 'dark')}
      >
        {isDark ? <IconSun size={18} /> : <IconMoon size={18} />}
      </ActionIcon>
    </Tooltip>
  );
}
