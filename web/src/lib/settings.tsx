"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

export interface Machine {
  id: string;
  name: string;
  baseUrl: string;
  token: string;
}

export interface Settings {
  baseUrl: string;
  token: string;
}

interface SettingsContextValue {
  machines: Machine[];
  activeMachine: Machine | null;
  settings: Settings | null;
  setActiveMachineId: (id: string) => void;
  addMachine: (machine: Omit<Machine, "id">) => void;
  updateMachine: (id: string, patch: Omit<Machine, "id">) => void;
  removeMachine: (id: string) => void;
}

const MACHINES_KEY = "warden.machines";
const ACTIVE_ID_KEY = "warden.activeMachineId";
const LEGACY_SETTINGS_KEY = "warden.settings";

function makeId(): string {
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

function loadInitialState(): { machines: Machine[]; activeMachineId: string | null } {
  const rawMachines = localStorage.getItem(MACHINES_KEY);
  if (rawMachines) {
    const machines: Machine[] = JSON.parse(rawMachines);
    const activeMachineId = localStorage.getItem(ACTIVE_ID_KEY);
    return { machines, activeMachineId };
  }

  const legacy = localStorage.getItem(LEGACY_SETTINGS_KEY);
  if (legacy) {
    const parsed: Settings = JSON.parse(legacy);
    const machine: Machine = { id: makeId(), name: "Minha máquina", ...parsed };
    localStorage.setItem(MACHINES_KEY, JSON.stringify([machine]));
    localStorage.setItem(ACTIVE_ID_KEY, machine.id);
    localStorage.removeItem(LEGACY_SETTINGS_KEY);
    return { machines: [machine], activeMachineId: machine.id };
  }

  return { machines: [], activeMachineId: null };
}

const SettingsContext = createContext<SettingsContextValue | null>(null);

export function SettingsProvider({ children }: { children: ReactNode }) {
  const [machines, setMachinesState] = useState<Machine[]>([]);
  const [activeMachineId, setActiveMachineIdState] = useState<string | null>(null);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    const initial = loadInitialState();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- localStorage só existe no client, precisa ler pós-mount
    setMachinesState(initial.machines);
    setActiveMachineIdState(initial.activeMachineId);
    setLoaded(true);
  }, []);

  function persistMachines(next: Machine[]) {
    localStorage.setItem(MACHINES_KEY, JSON.stringify(next));
    setMachinesState(next);
  }

  function setActiveMachineId(id: string) {
    localStorage.setItem(ACTIVE_ID_KEY, id);
    setActiveMachineIdState(id);
  }

  function addMachine(machine: Omit<Machine, "id">) {
    const created: Machine = { id: makeId(), ...machine };
    persistMachines([...machines, created]);
    setActiveMachineId(created.id);
  }

  function updateMachine(id: string, patch: Omit<Machine, "id">) {
    persistMachines(machines.map((m) => (m.id === id ? { id, ...patch } : m)));
  }

  function removeMachine(id: string) {
    const next = machines.filter((m) => m.id !== id);
    persistMachines(next);
    if (activeMachineId === id) {
      const fallback = next[0]?.id ?? null;
      if (fallback) setActiveMachineId(fallback);
      else {
        localStorage.removeItem(ACTIVE_ID_KEY);
        setActiveMachineIdState(null);
      }
    }
  }

  if (!loaded) return null;

  const activeMachine = machines.find((m) => m.id === activeMachineId) ?? null;
  const settings = activeMachine ? { baseUrl: activeMachine.baseUrl, token: activeMachine.token } : null;

  return (
    <SettingsContext.Provider
      value={{
        machines,
        activeMachine,
        settings,
        setActiveMachineId,
        addMachine,
        updateMachine,
        removeMachine,
      }}
    >
      {children}
    </SettingsContext.Provider>
  );
}

export function useSettings() {
  const ctx = useContext(SettingsContext);
  if (!ctx) throw new Error("useSettings precisa estar dentro de SettingsProvider");
  return ctx;
}
