"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

/**
 * "python" = engine FastAPI atual (WebSocket cru pra log). "agent" = Warden.Agent em C# (SignalR).
 * Os dois falam o mesmo contrato REST pros endpoints já portados (NEW_CONTEXT.md §12 fase 5-6) —
 * só o transporte de log ao vivo diverge, por isso o componente de log precisa saber qual é.
 */
export type BackendKind = "python" | "agent";

export interface Machine {
  id: string;
  name: string;
  baseUrl: string;
  token: string;
  kind: BackendKind;
}

export interface Settings {
  baseUrl: string;
  token: string;
  kind: BackendKind;
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
  try {
    const rawMachines = localStorage.getItem(MACHINES_KEY);
    if (rawMachines) {
      // máquinas salvas antes da fase 7 não têm `kind` no localStorage — assume "python" (era o único
      // backend até então). Tipado como parcial de propósito: o JSON gravado antes dessa migração
      // não bate com o `Machine` atual, mesmo que o compilador não veja essa diferença de versão.
      const raw: Array<Omit<Machine, "kind"> & { kind?: BackendKind }> = JSON.parse(rawMachines);
      const machines: Machine[] = raw.map((m) => ({ kind: "python", ...m }));
      const activeMachineId = localStorage.getItem(ACTIVE_ID_KEY);
      return { machines, activeMachineId };
    }

    const legacy = localStorage.getItem(LEGACY_SETTINGS_KEY);
    if (legacy) {
      // blob antigo de antes da fase 6 (single-machine) e antes da fase 7 (kind) — nenhuma das duas
      // garantida presente, só `baseUrl`/`token` são certos de existir.
      const parsed: Pick<Settings, "baseUrl" | "token"> = JSON.parse(legacy);
      const machine: Machine = { id: makeId(), name: "Minha máquina", kind: "python", ...parsed };
      localStorage.setItem(MACHINES_KEY, JSON.stringify([machine]));
      localStorage.setItem(ACTIVE_ID_KEY, machine.id);
      localStorage.removeItem(LEGACY_SETTINGS_KEY);
      return { machines: [machine], activeMachineId: machine.id };
    }
  } catch {
    // dado corrompido no localStorage: cai pro estado vazio em vez de travar hidratação
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
  const settings = activeMachine
    ? { baseUrl: activeMachine.baseUrl, token: activeMachine.token, kind: activeMachine.kind }
    : null;

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
