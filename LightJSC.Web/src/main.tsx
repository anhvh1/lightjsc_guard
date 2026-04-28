import '@mantine/core/styles.css';
import '@mantine/dates/styles.css';
import '@mantine/notifications/styles.css';
import '@fontsource/roboto/400.css';
import '@fontsource/roboto/500.css';
import '@fontsource/roboto/700.css';
import './styles/theme.css';
import 'dayjs/locale/en';
import 'dayjs/locale/vi';

import React from 'react';
import ReactDOM from 'react-dom/client';
import {
  ColorSchemeScript,
  createTheme,
  localStorageColorSchemeManager,
  MantineProvider
} from '@mantine/core';
import { ModalsProvider } from '@mantine/modals';
import { Notifications } from '@mantine/notifications';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import App from './App';
import { I18nProvider } from './i18n/I18nProvider';

const theme = createTheme({
  fontFamily: '"Roboto", "Segoe UI", sans-serif',
  headings: { fontFamily: '"Roboto", "Segoe UI", sans-serif', fontWeight: '600' },
  primaryColor: 'brand',
  defaultRadius: 'md',
  colors: {
    brand: [
      '#fff1e8',
      '#ffd9c2',
      '#ffb98e',
      '#ff985a',
      '#ff7c2f',
      '#f36f21',
      '#d85b15',
      '#b0480f',
      '#8a370a',
      '#662806'
    ],
    amber: [
      '#fff6e5',
      '#ffe4bf',
      '#ffd296',
      '#ffbf6d',
      '#ffad44',
      '#f7941d',
      '#d07300',
      '#a65800',
      '#7d4000',
      '#553000'
    ]
  }
});

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false
    }
  }
});

const colorSchemeManager = localStorageColorSchemeManager({ key: 'ipro-color-scheme' });

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <ColorSchemeScript defaultColorScheme="dark" localStorageKey="ipro-color-scheme" />
    <MantineProvider theme={theme} colorSchemeManager={colorSchemeManager} defaultColorScheme="dark">
      <QueryClientProvider client={queryClient}>
        <ModalsProvider>
          <Notifications position="top-right" />
          <BrowserRouter>
            <I18nProvider>
              <App />
            </I18nProvider>
          </BrowserRouter>
        </ModalsProvider>
      </QueryClientProvider>
    </MantineProvider>
  </React.StrictMode>
);
