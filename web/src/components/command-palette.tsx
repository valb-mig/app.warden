"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import { useTheme } from "next-themes";
import {
  ArrowDownToLine,
  ArrowUpFromLine,
  GitBranch,
  Moon,
  Play,
  RefreshCw,
  Search,
  Server,
  Square,
  Sun,
  Wrench,
} from "lucide-react";
import { toast } from "sonner";

import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog";
import { api, ApiError, type Action, type GitInfo, type Project, type ProjectStatus } from "@/lib/api";
import { useSettings } from "@/lib/settings";

const OPEN_EVENT = "warden:open-command-palette";

interface PaletteItem {
  id: string;
  section: string;
  label: string;
  hint?: string;
  icon: React.ReactNode;
  keywords?: string;
  confirm?: boolean;
  execute: () => void | Promise<void>;
}

function normalize(s: string): string {
  return s.toLowerCase();
}

export function CommandPalette() {
  const { settings, machines, activeMachine, setActiveMachineId } = useSettings();
  const router = useRouter();
  const pathname = usePathname();
  const { resolvedTheme, setTheme } = useTheme();

  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [armed, setArmed] = useState<{ label: string; execute: () => void | Promise<void> } | null>(
    null,
  );

  const [projects, setProjects] = useState<Project[]>([]);
  const [statuses, setStatuses] = useState<Record<string, ProjectStatus>>({});
  const [contextActions, setContextActions] = useState<Action[]>([]);
  const [contextGit, setContextGit] = useState<GitInfo | null>(null);

  const inputRef = useRef<HTMLInputElement>(null);
  const config = settings;
  const contextProjectId = pathname?.match(/^\/projects\/([^/]+)/)?.[1] ?? null;

  useEffect(() => {
    function onKeydown(e: KeyboardEvent) {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setOpen((o) => !o);
      }
    }
    function onOpenEvent() {
      setOpen(true);
    }
    window.addEventListener("keydown", onKeydown);
    window.addEventListener(OPEN_EVENT, onOpenEvent);
    return () => {
      window.removeEventListener("keydown", onKeydown);
      window.removeEventListener(OPEN_EVENT, onOpenEvent);
    };
  }, []);

  useEffect(() => {
    if (!open || !config) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reset ao abrir, sincroniza com prop externa `open`
    setQuery("");
    setSelectedIndex(0);
    setArmed(null);

    let cancelled = false;
    api
      .listProjects(config)
      .then(async (list) => {
        if (cancelled) return;
        setProjects(list);
        const entries = await Promise.all(
          list.map(async (p) => [p.id, await api.status(config, p.id).catch(() => null)] as const),
        );
        if (cancelled) return;
        setStatuses(Object.fromEntries(entries.filter(([, s]) => s !== null)) as Record<
          string,
          ProjectStatus
        >);
      })
      .catch(() => {});

    if (contextProjectId) {
      api
        .listActions(config, contextProjectId)
        .then((a) => !cancelled && setContextActions(a))
        .catch(() => !cancelled && setContextActions([]));
      api
        .git(config, contextProjectId)
        .then((g) => !cancelled && setContextGit(g))
        .catch(() => !cancelled && setContextGit(null));
    } else {
      setContextActions([]);
      setContextGit(null);
    }

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, contextProjectId]);

  const items = useMemo<PaletteItem[]>(() => {
    if (!config) return [];
    const list: PaletteItem[] = [];

    for (const project of projects) {
      list.push({
        id: `nav-${project.id}`,
        section: "Projetos",
        label: project.id,
        hint: project.group ?? project.type,
        icon: <Search className="size-4" />,
        execute: () => router.push(`/projects/${project.id}`),
      });

      const running = statuses[project.id]?.running ?? false;
      list.push({
        id: `power-${project.id}`,
        section: "Projetos",
        label: `${running ? "Parar" : "Iniciar"} ${project.id}`,
        icon: running ? <Square className="size-4" /> : <Play className="size-4" />,
        keywords: "start stop iniciar parar",
        execute: async () => {
          try {
            await (running ? api.stop(config, project.id) : api.start(config, project.id));
            toast.success(running ? "stop disparado" : "start disparado");
          } catch (err) {
            toast.error(err instanceof ApiError ? err.message : "ação falhou");
          }
        },
      });
    }

    if (contextProjectId) {
      for (const action of contextActions) {
        if (action.interactive) continue;
        list.push({
          id: `action-${action.name}`,
          section: "Ações (projeto atual)",
          label: action.name,
          icon: <Wrench className="size-4" />,
          confirm: true,
          execute: async () => {
            try {
              const res = await api.runAction(config, contextProjectId, action.name);
              if (res.exit_code === 0) toast.success(`${action.name}: concluído`);
              else toast.error(`${action.name}: saiu com código ${res.exit_code}`);
            } catch (err) {
              toast.error(err instanceof ApiError ? err.message : `${action.name}: ação falhou`);
            }
          },
        });
      }

      if (contextGit) {
        const runGit = async (verb: "fetch" | "sync" | "pull" | "push", confirmed: boolean) => {
          try {
            const res = await api.gitCommand(config, contextProjectId, verb, confirmed);
            if (res.refused) toast.error(res.output || `${verb}: bloqueado`);
            else if (res.ok) toast.success(`${verb}: ${res.output || "concluído"}`);
            else toast.error(res.output || `${verb}: falhou`);
          } catch (err) {
            toast.error(err instanceof ApiError ? err.message : `${verb}: falhou`);
          }
        };
        list.push({
          id: "git-fetch",
          section: "Git (projeto atual)",
          label: "git fetch",
          icon: <RefreshCw className="size-4" />,
          execute: () => runGit("fetch", false),
        });
        list.push({
          id: "git-sync",
          section: "Git (projeto atual)",
          label: "git sync",
          hint: "fetch + fast-forward",
          icon: <GitBranch className="size-4" />,
          execute: () => runGit("sync", false),
        });
        list.push({
          id: "git-pull",
          section: "Git (projeto atual)",
          label: "git pull",
          icon: <ArrowDownToLine className="size-4" />,
          confirm: true,
          execute: () => runGit("pull", true),
        });
        list.push({
          id: "git-push",
          section: "Git (projeto atual)",
          label: "git push",
          icon: <ArrowUpFromLine className="size-4" />,
          confirm: true,
          execute: () => runGit("push", true),
        });
      }
    }

    for (const machine of machines) {
      if (machine.id === activeMachine?.id) continue;
      list.push({
        id: `machine-${machine.id}`,
        section: "Geral",
        label: `Trocar pra ${machine.name}`,
        icon: <Server className="size-4" />,
        keywords: "maquina machine trocar conectar",
        execute: () => setActiveMachineId(machine.id),
      });
    }

    const isDark = resolvedTheme === "dark";
    list.push({
      id: "theme-toggle",
      section: "Geral",
      label: isDark ? "Ativar tema claro" : "Ativar tema escuro",
      icon: isDark ? <Sun className="size-4" /> : <Moon className="size-4" />,
      keywords: "tema dark light theme",
      execute: () => setTheme(isDark ? "light" : "dark"),
    });

    return list;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config, projects, statuses, contextProjectId, contextActions, contextGit, machines, activeMachine, resolvedTheme]);

  const filtered = useMemo(() => {
    const q = normalize(query);
    if (!q) return items;
    return items.filter((i) => normalize(`${i.label} ${i.hint ?? ""} ${i.keywords ?? ""}`).includes(q));
  }, [items, query]);

  function onQueryChange(value: string) {
    setQuery(value);
    setSelectedIndex(0);
  }

  async function runItem(item: PaletteItem) {
    if (item.confirm) {
      setArmed({ label: item.label, execute: item.execute });
      return;
    }
    await item.execute();
    setOpen(false);
  }

  async function confirmArmed() {
    if (!armed) return;
    await armed.execute();
    setArmed(null);
    setOpen(false);
  }

  function onKeyDown(e: React.KeyboardEvent) {
    if (armed) {
      if (e.key === "Enter") {
        e.preventDefault();
        confirmArmed();
      } else if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        setArmed(null);
      }
      return;
    }
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setSelectedIndex((i) => Math.min(i + 1, filtered.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setSelectedIndex((i) => Math.max(i - 1, 0));
    } else if (e.key === "Enter") {
      e.preventDefault();
      const item = filtered[selectedIndex];
      if (item) runItem(item);
    }
  }

  let lastSection = "";

  if (!config) return null;

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) setArmed(null);
      }}
    >
      <DialogContent
        showCloseButton={false}
        className="top-[20%] max-w-lg translate-y-0 gap-0 p-0 sm:max-w-lg"
        initialFocus={inputRef}
      >
        <DialogTitle className="sr-only">Command palette</DialogTitle>
        <div className="flex items-center gap-2 border-b px-3 py-2.5">
          <Search className="size-4 shrink-0 text-muted-foreground" />
          <input
            ref={inputRef}
            value={query}
            onChange={(e) => onQueryChange(e.target.value)}
            onKeyDown={onKeyDown}
            placeholder="Buscar projeto, ação, comando..."
            className="min-w-0 flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground"
          />
        </div>

        {armed ? (
          <div
            className="flex cursor-pointer items-center justify-between px-3 py-2.5 text-sm"
            onClick={confirmArmed}
          >
            <span>
              Confirmar: <strong>{armed.label}</strong>
            </span>
            <span className="text-xs text-muted-foreground">Enter executa · Esc cancela</span>
          </div>
        ) : (
          <div className="max-h-80 overflow-y-auto p-1">
            {filtered.length === 0 && (
              <p className="px-3 py-6 text-center text-sm text-muted-foreground">
                nada encontrado
              </p>
            )}
            {filtered.map((item, i) => {
              const showHeading = item.section !== lastSection;
              lastSection = item.section;
              return (
                <div key={item.id}>
                  {showHeading && (
                    <p className="px-2 pt-2 pb-1 text-xs font-medium text-muted-foreground">
                      {item.section}
                    </p>
                  )}
                  <button
                    type="button"
                    onMouseEnter={() => setSelectedIndex(i)}
                    onClick={() => runItem(item)}
                    className={`flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm ${
                      i === selectedIndex ? "bg-accent text-accent-foreground" : ""
                    }`}
                  >
                    {item.icon}
                    <span className="min-w-0 flex-1 truncate">{item.label}</span>
                    {item.hint && (
                      <span className="shrink-0 text-xs text-muted-foreground">{item.hint}</span>
                    )}
                  </button>
                </div>
              );
            })}
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

export function openCommandPalette() {
  window.dispatchEvent(new Event(OPEN_EVENT));
}
