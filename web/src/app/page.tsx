"use client";

import Link from "next/link";
import { Fragment, useCallback, useEffect, useMemo, useState } from "react";
import {
  AlertTriangle,
  ChevronRight,
  FolderOpen,
  Loader2,
  Play,
  RefreshCw,
  Search,
  Square,
  X,
} from "lucide-react";
import { toast } from "sonner";

import { ConnectCard } from "@/components/connect-card";
import { LanguageIcons } from "@/components/language-icons";
import { SystemVitalsCard } from "@/components/system-vitals-card";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  api,
  ApiError,
  type GitInfo,
  type HistoryEvent,
  type Project,
  type ProjectStatus,
} from "@/lib/api";
import { useSettings } from "@/lib/settings";

const STATUS_POLL_MS = 3000;
const GIT_POLL_MS = 15000;
const DISMISSED_KEY = "warden.dismissedAttention";

function loadDismissed(): Set<string> {
  try {
    const raw = localStorage.getItem(DISMISSED_KEY);
    return raw ? new Set(JSON.parse(raw)) : new Set();
  } catch {
    return new Set();
  }
}

export default function Dashboard() {
  const { settings } = useSettings();

  if (!settings) return <ConnectCard />;

  return <ProjectList baseUrl={settings.baseUrl} token={settings.token} />;
}

function ProjectList({ baseUrl, token }: { baseUrl: string; token: string }) {
  const config = { baseUrl, token };
  const [projects, setProjects] = useState<Project[] | null>(null);
  const [statuses, setStatuses] = useState<Record<string, ProjectStatus>>({});
  const [gitInfo, setGitInfo] = useState<Record<string, GitInfo | null>>({});
  const [lastEvents, setLastEvents] = useState<Record<string, HistoryEvent | null>>({});
  const [pending, setPending] = useState<Record<string, boolean>>({});
  const [dismissed, setDismissed] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState("");

  const loadProjects = useCallback(async () => {
    try {
      const list = await api.listProjects(config);
      setProjects(list);
      setError(null);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "falha ao conectar no Warden");
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [baseUrl, token]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- fetch inicial ao montar
    loadProjects();
  }, [loadProjects]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- lê localStorage só existe no client, após montar
    setDismissed(loadDismissed());
  }, []);

  function dismissAttention(key: string) {
    setDismissed((prev) => {
      const next = new Set(prev);
      next.add(key);
      localStorage.setItem(DISMISSED_KEY, JSON.stringify([...next]));
      return next;
    });
  }

  useEffect(() => {
    // projeto novo/editado no SyncDialog (header, fora dessa árvore) — recarrega a lista.
    window.addEventListener("warden:project-configured", loadProjects);
    return () => window.removeEventListener("warden:project-configured", loadProjects);
  }, [loadProjects]);

  useEffect(() => {
    if (!projects || projects.length === 0) return;

    let cancelled = false;
    const poll = async () => {
      const entries = await Promise.all(
        projects.map(async (p) => {
          try {
            return [p.id, await api.status(config, p.id)] as const;
          } catch {
            return null;
          }
        }),
      );
      if (cancelled) return;
      setStatuses((prev) => {
        const next = { ...prev };
        for (const entry of entries) {
          if (entry) next[entry[0]] = entry[1];
        }
        return next;
      });
    };

    poll();
    const interval = setInterval(poll, STATUS_POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projects, baseUrl, token]);

  useEffect(() => {
    if (!projects || projects.length === 0) return;

    let cancelled = false;
    const poll = async () => {
      const entries = await Promise.all(
        projects.map(async (p) => {
          const [git, history] = await Promise.all([
            api.git(config, p.id).catch(() => null),
            api.history(config, p.id, 1).catch(() => []),
          ]);
          return [p.id, git, history[0] ?? null] as const;
        }),
      );
      if (cancelled) return;
      setGitInfo((prev) => {
        const next = { ...prev };
        for (const [id, git] of entries) next[id] = git;
        return next;
      });
      setLastEvents((prev) => {
        const next = { ...prev };
        for (const [id, , event] of entries) next[id] = event;
        return next;
      });
    };

    poll();
    const interval = setInterval(poll, GIT_POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projects, baseUrl, token]);

  async function handleToggle(project: Project) {
    const running = statuses[project.id]?.running ?? false;
    setPending((prev) => ({ ...prev, [project.id]: true }));
    try {
      if (running) {
        await api.stop(config, project.id);
        toast.success(`${project.name}: stop disparado`);
      } else {
        await api.start(config, project.id);
        toast.success(`${project.name}: start disparado`);
      }
      const status = await api.status(config, project.id);
      setStatuses((prev) => ({ ...prev, [project.id]: status }));
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "ação falhou");
    } finally {
      setPending((prev) => ({ ...prev, [project.id]: false }));
    }
  }

  const runningCount = projects
    ? projects.filter((p) => statuses[p.id]?.running).length
    : 0;

  const attentionItems = useMemo(() => {
    if (!projects) return [];
    const items: { key: string; projectId: string; label: string }[] = [];
    for (const project of projects) {
      const event = lastEvents[project.id];
      const git = gitInfo[project.id];
      if (event?.type === "error") {
        items.push({
          key: `${project.id}-error-${event.created_at}`,
          projectId: project.id,
          label: `${project.name}: parou com erro`,
        });
      }
      if (git?.dirty) {
        items.push({
          key: `${project.id}-dirty-${git.dirty_count}`,
          projectId: project.id,
          label: `${project.name}: ${git.dirty_count} arquivo(s) não commitado(s)`,
        });
      }
      const behind = git?.behind ?? 0;
      if (behind > 0) {
        items.push({
          key: `${project.id}-behind-${behind}`,
          projectId: project.id,
          label: `${project.name}: ${behind} commit(s) atrás do origin`,
        });
      }
    }
    return items;
  }, [projects, gitInfo, lastEvents]);

  const visibleAttentionItems = useMemo(
    () => attentionItems.filter((item) => !dismissed.has(item.key)),
    [attentionItems, dismissed],
  );

  const filtered = useMemo(() => {
    if (!projects) return [];
    const q = query.trim().toLowerCase();
    if (!q) return projects;
    return projects.filter(
      (p) =>
        p.name.toLowerCase().includes(q) ||
        p.id.toLowerCase().includes(q) ||
        p.type.toLowerCase().includes(q) ||
        (p.group ?? "").toLowerCase().includes(q),
    );
  }, [projects, query]);

  const groups = useMemo(() => {
    const byGroup = new Map<string, Project[]>();
    for (const project of filtered) {
      const key = project.group || "";
      const list = byGroup.get(key) ?? [];
      list.push(project);
      byGroup.set(key, list);
    }
    return [...byGroup.entries()];
  }, [filtered]);

  return (
    <div className="flex flex-col gap-4 p-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-lg font-semibold">Projetos</h1>
          <p className="text-sm text-muted-foreground">
            Hub local de supervisão de processos e containers
          </p>
        </div>
        {projects && projects.length > 0 && (
          <Badge variant={runningCount > 0 ? "default" : "secondary"}>
            {runningCount} de {projects.length} rodando
          </Badge>
        )}
      </div>

      <SystemVitalsCard config={config} />

      {visibleAttentionItems.length > 0 && (
        <Alert variant="destructive">
          <AlertTriangle />
          <AlertTitle>Precisa de atenção</AlertTitle>
          <AlertDescription>
            <ul className="flex flex-col gap-1">
              {visibleAttentionItems.map((item) => (
                <li key={item.key} className="flex items-center justify-between gap-2">
                  <Link href={`/projects/${item.projectId}`} className="hover:underline">
                    {item.label}
                  </Link>
                  <button
                    type="button"
                    onClick={() => dismissAttention(item.key)}
                    className="shrink-0 text-muted-foreground hover:text-foreground"
                    aria-label="Dispensar alerta"
                  >
                    <X className="size-3.5" />
                  </button>
                </li>
              ))}
            </ul>
          </AlertDescription>
        </Alert>
      )}

      {!error && projects && projects.length > 0 && (
        <div className="relative max-w-sm">
          <Search className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="buscar projeto..."
            className="h-8 pl-7"
          />
          {query && (
            <button
              type="button"
              onClick={() => setQuery("")}
              className="absolute top-1/2 right-2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
            >
              <X className="size-3.5" />
            </button>
          )}
        </div>
      )}

      {error && (
        <Alert variant="destructive">
          <AlertTriangle />
          <AlertTitle>Falha ao conectar</AlertTitle>
          <AlertDescription className="flex items-center justify-between gap-3">
            <span>{error}</span>
            <Button size="sm" variant="outline" onClick={loadProjects}>
              <RefreshCw className="size-4" />
              Tentar de novo
            </Button>
          </AlertDescription>
        </Alert>
      )}

      {!error && !projects && (
        <div className="flex flex-col gap-3">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
        </div>
      )}

      {!error && projects && projects.length === 0 && (
        <Card>
          <CardContent className="flex flex-col items-center gap-2 py-10 text-center text-muted-foreground">
            <FolderOpen className="size-8" />
            <p>
              Nenhum projeto configurado em <code className="mx-1">~/.warden/</code>.
            </p>
          </CardContent>
        </Card>
      )}

      {!error && projects && projects.length > 0 && filtered.length === 0 && (
        <p className="text-sm text-muted-foreground">nenhum projeto bate com a busca</p>
      )}

      {!error && filtered.length > 0 && (
        <Card className="flex-1 gap-0 overflow-hidden py-0">
          <CardContent className="max-h-[70vh] overflow-y-auto px-0">
            <Table>
              <TableHeader className="sticky top-0 z-10 bg-card">
                <TableRow>
                  <TableHead>Projeto</TableHead>
                  <TableHead>Tipo</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Git</TableHead>
                  <TableHead>Portas</TableHead>
                  <TableHead className="text-right">Ações</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {groups.map(([group, groupProjects]) => (
                  <Fragment key={group || "__ungrouped"}>
                    {group && (
                      <TableRow className="hover:bg-transparent">
                        <TableCell
                          colSpan={6}
                          className="bg-muted/30 py-1.5 text-xs font-medium text-muted-foreground"
                        >
                          {group}
                        </TableCell>
                      </TableRow>
                    )}
                    {groupProjects.map((project) => {
                      const status = statuses[project.id];
                      const running = status?.running ?? false;
                      const isPending = pending[project.id] ?? false;
                      return (
                        <TableRow key={project.id}>
                          <TableCell>
                            <Link
                              href={`/projects/${project.id}`}
                              className="flex items-center gap-2 font-medium hover:underline"
                            >
                              {project.name}
                              <LanguageIcons config={config} projectId={project.id} />
                              <ChevronRight className="size-3.5 text-muted-foreground" />
                            </Link>
                          </TableCell>
                          <TableCell>
                            <Badge variant="outline">{project.type}</Badge>
                          </TableCell>
                          <TableCell>
                            {status ? (
                              <Badge variant={running ? "default" : "secondary"}>
                                {running ? "rodando" : "parado"}
                              </Badge>
                            ) : (
                              <Skeleton className="h-5 w-16" />
                            )}
                          </TableCell>
                          <TableCell>
                            {project.id in gitInfo ? (
                              (() => {
                                const git = gitInfo[project.id];
                                if (!git) return <span className="text-muted-foreground">—</span>;
                                if (git.dirty)
                                  return <Badge variant="destructive">sujo ({git.dirty_count})</Badge>;
                                if ((git.behind ?? 0) > 0)
                                  return <Badge variant="secondary">atrás ({git.behind})</Badge>;
                                return <Badge variant="outline">limpo</Badge>;
                              })()
                            ) : (
                              <Skeleton className="h-5 w-14" />
                            )}
                          </TableCell>
                          <TableCell className="text-muted-foreground">
                            {status?.ports.join(", ") || "—"}
                          </TableCell>
                          <TableCell className="text-right">
                            <Button
                              size="sm"
                              variant={running ? "destructive" : "default"}
                              disabled={isPending || !status}
                              onClick={() => handleToggle(project)}
                            >
                              {isPending ? (
                                <Loader2 className="size-4 animate-spin" />
                              ) : running ? (
                                <Square className="size-4" />
                              ) : (
                                <Play className="size-4" />
                              )}
                              {running ? "Parar" : "Iniciar"}
                            </Button>
                          </TableCell>
                        </TableRow>
                      );
                    })}
                  </Fragment>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
