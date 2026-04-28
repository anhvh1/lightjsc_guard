import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';

type Language = 'vi' | 'en';
type Messages = Record<string, unknown>;

type TranslateParams = Record<string, string | number>;

type I18nContextValue = {
  language: Language;
  setLanguage: (language: Language) => void;
  t: (key: string, params?: TranslateParams, fallback?: string) => string;
  ready: boolean;
};

const STORAGE_KEY = 'ipro-language';
const DEFAULT_LANGUAGE: Language = 'vi';

const I18nContext = createContext<I18nContextValue>({
  language: DEFAULT_LANGUAGE,
  setLanguage: () => undefined,
  t: (key) => key,
  ready: false
});

const normalizeBaseUrl = () => {
  const base = (import.meta.env.BASE_URL as string | undefined) ?? '/';
  return base.endsWith('/') ? base : `${base}/`;
};

const loadMessages = async (language: Language): Promise<Messages> => {
  const url = `${normalizeBaseUrl()}i18n/${language}.json`;
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`Failed to load i18n file: ${language}`);
  }
  return (await response.json()) as Messages;
};

const resolvePath = (messages: Messages, key: string) => {
  return key.split('.').reduce<unknown>((value, part) => {
    if (value && typeof value === 'object' && part in (value as Record<string, unknown>)) {
      return (value as Record<string, unknown>)[part];
    }
    return undefined;
  }, messages);
};

const interpolate = (text: string, params?: TranslateParams) => {
  if (!params) {
    return text;
  }
  return Object.entries(params).reduce((acc, [key, value]) => {
    return acc.replaceAll(`{${key}}`, String(value));
  }, text);
};

const getStoredLanguage = (): Language => {
  if (typeof window === 'undefined') {
    return DEFAULT_LANGUAGE;
  }
  const stored = window.localStorage.getItem(STORAGE_KEY);
  if (stored === 'en' || stored === 'vi') {
    return stored;
  }
  return DEFAULT_LANGUAGE;
};

export function I18nProvider({ children }: { children: React.ReactNode }) {
  const [language, setLanguageState] = useState<Language>(getStoredLanguage);
  const [messages, setMessages] = useState<Messages>({});
  const [ready, setReady] = useState(false);

  const setLanguage = useCallback((next: Language) => {
    setLanguageState(next);
    if (typeof window !== 'undefined') {
      window.localStorage.setItem(STORAGE_KEY, next);
    }
  }, []);

  useEffect(() => {
    let active = true;
    setReady(false);
    loadMessages(language)
      .then((data) => {
        if (!active) return;
        setMessages(data);
      })
      .catch(() => {
        if (!active) return;
        setMessages({});
      })
      .finally(() => {
        if (!active) return;
        setReady(true);
      });

    return () => {
      active = false;
    };
  }, [language]);

  const t = useCallback(
    (key: string, params?: TranslateParams, fallback?: string) => {
      const value = resolvePath(messages, key);
      if (typeof value === 'string') {
        return interpolate(value, params);
      }
      if (fallback) {
        return interpolate(fallback, params);
      }
      return key;
    },
    [messages]
  );

  const contextValue = useMemo(
    () => ({
      language,
      setLanguage,
      t,
      ready
    }),
    [language, ready, setLanguage, t]
  );

  return <I18nContext.Provider value={contextValue}>{children}</I18nContext.Provider>;
}

export function useI18n() {
  return useContext(I18nContext);
}
