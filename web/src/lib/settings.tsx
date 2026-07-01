"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

export interface Settings {
  baseUrl: string;
  token: string;
}

interface SettingsContextValue {
  settings: Settings | null;
  setSettings: (settings: Settings) => void;
  clear: () => void;
}

const STORAGE_KEY = "warden.settings";

const SettingsContext = createContext<SettingsContextValue | null>(null);

export function SettingsProvider({ children }: { children: ReactNode }) {
  const [settings, setSettingsState] = useState<Settings | null>(null);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    const raw = localStorage.getItem(STORAGE_KEY);
    // eslint-disable-next-line react-hooks/set-state-in-effect -- localStorage só existe no client, precisa ler pós-mount
    if (raw) setSettingsState(JSON.parse(raw));
    setLoaded(true);
  }, []);

  const setSettings = (value: Settings) => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(value));
    setSettingsState(value);
  };

  const clear = () => {
    localStorage.removeItem(STORAGE_KEY);
    setSettingsState(null);
  };

  if (!loaded) return null;

  return (
    <SettingsContext.Provider value={{ settings, setSettings, clear }}>
      {children}
    </SettingsContext.Provider>
  );
}

export function useSettings() {
  const ctx = useContext(SettingsContext);
  if (!ctx) throw new Error("useSettings precisa estar dentro de SettingsProvider");
  return ctx;
}
