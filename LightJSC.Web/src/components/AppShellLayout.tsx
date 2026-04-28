import {
  AppShell,
  Burger,
  Group,
  HoverCard,
  NavLink,
  Stack,
  Text,
  Title,
  useMantineTheme
} from '@mantine/core';
import { useDisclosure, useMediaQuery } from '@mantine/hooks';
import {
  IconActivity,
  IconCamera,
  IconCalculator,
  IconCalendarStats,
  IconLayoutDashboard,
  IconMap,
  IconRadar,
  IconSettings,
  IconSparkles,
  IconUser,
  IconWebhook
} from '@tabler/icons-react';
import { Link, useLocation } from 'react-router-dom';
import { useI18n } from '../i18n/I18nProvider';
import { LanguageToggle } from './LanguageToggle';
import { ThemeToggle } from './ThemeToggle';

const navItems = [
  {
    key: 'dashboard',
    to: '/',
    icon: IconLayoutDashboard
  },
  {
    key: 'welcome',
    to: '/welcome',
    icon: IconSparkles
  },
  {
    key: 'attendance',
    to: '/attendance',
    icon: IconCalendarStats
  },
  {
    key: 'sizing',
    to: '/sizing',
    icon: IconCalculator
  },
  {
    key: 'faceStream',
    to: '/face-stream',
    icon: IconActivity
  },
  {
    key: 'faceTrace',
    to: '/face-trace',
    icon: IconRadar
  },
  {
    key: 'cameras',
    to: '/cameras',
    icon: IconCamera
  },
  {
    key: 'maps',
    to: '/maps',
    icon: IconMap
  },
  {
    key: 'persons',
    to: '/persons',
    icon: IconUser
  },
  {
    key: 'subscribers',
    to: '/subscribers',
    icon: IconWebhook
  },
  {
    key: 'settings',
    to: '/settings',
    icon: IconSettings
  }
];

export function AppShellLayout({ children }: { children: React.ReactNode }) {
  const theme = useMantineTheme();
  const isMobile = useMediaQuery(`(max-width: ${theme.breakpoints.sm})`);
  const [desktopOpened, desktopHandlers] = useDisclosure(true);
  const [mobileOpened, mobileHandlers] = useDisclosure(false);
  const navbarOpened = isMobile ? mobileOpened : desktopOpened;
  const isDesktopCollapsed = !desktopOpened && !isMobile;
  const location = useLocation();
  const { t } = useI18n();
  const navbarWidth = isMobile ? 280 : isDesktopCollapsed ? 76 : 280;

  return (
    <div className="app-root">
      <div className="app-background">
        <span className="orb orb-a" />
        <span className="orb orb-b" />
        <span className="orb orb-c" />
      </div>

      <AppShell
        className="app-shell"
        header={{ height: { base: 112, sm: 72 } }}
        navbar={{
          width: navbarWidth,
          breakpoint: 'sm',
          collapsed: { mobile: !mobileOpened, desktop: false }
        }}
        padding="md"
      >
        <AppShell.Header className="app-header">
          <Group h="100%" px="md" justify="space-between">
            <Group gap="md" wrap="nowrap" style={{ flex: 1, minWidth: 0 }}>
                <Burger
                  opened={navbarOpened}
                  onClick={() =>
                    isMobile ? mobileHandlers.toggle() : desktopHandlers.toggle()
                  }
                  size="sm"
                  aria-label={t('common.actions.toggleSidebar')}
                />
              <div className="brand-block">
                <div className="brand-mark">
                  <img
                    src="/LightJSC Logo.png"
                    alt={t('app.logoAlt', undefined, 'LightJSC')}
                    className="brand-logo"
                  />
                </div>
                <Stack gap={2}>
                  <Group gap="xs" align="center" wrap="wrap">
                    <Title order={4} className="brand-title">{t('app.title')}</Title>
                  </Group>
                </Stack>
              </div>
            </Group>
            <Group gap="xs">
              <LanguageToggle />
              <ThemeToggle />
            </Group>
          </Group>
        </AppShell.Header>

        <AppShell.Navbar p={isDesktopCollapsed ? 'xs' : 'md'} data-collapsed={isDesktopCollapsed}>
          <Stack gap="xs">
            {navItems.map((item) => {
              const Icon = item.icon;
              const active =
                item.to === '/'
                  ? location.pathname === '/'
                  : location.pathname.startsWith(item.to);

              const label = t(`menu.${item.key}.label`);
              const description = t(`menu.${item.key}.desc`);
              const navLink = (
                <NavLink
                  component={Link}
                  to={item.to}
                  active={active}
                  label={label}
                  description={description}
                  leftSection={<Icon size={18} />}
                  className="surface-card"
                  aria-label={label}
                />
              );

              if (!isDesktopCollapsed) {
                return <div key={item.to}>{navLink}</div>;
              }

              return (
                <HoverCard
                  key={item.to}
                  position="right"
                  offset={12}
                  openDelay={120}
                  closeDelay={80}
                  withinPortal
                >
                  <HoverCard.Target>{navLink}</HoverCard.Target>
                  <HoverCard.Dropdown className="nav-flyout surface-card">
                    <Stack gap={2}>
                      <Text size="sm" fw={600}>
                        {label}
                      </Text>
                      <Text size="xs" className="muted-text">
                        {description}
                      </Text>
                    </Stack>
                  </HoverCard.Dropdown>
                </HoverCard>
              );
            })}
          </Stack>
        </AppShell.Navbar>

        <AppShell.Main className="app-main">{children}</AppShell.Main>
      </AppShell>
    </div>
  );
}

