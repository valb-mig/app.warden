"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import {
  AlertTriangle,
  ChevronRight,
  FolderOpen,
  Loader2,
  Play,
  RefreshCw,
  Square,
} from "lucide-react";
import { toast } from "sonner";

import { ConnectCard } from "@/components/connect-card";
import { LanguageIcons } from "@/components/language-icons";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { api, ApiError, type Project, type ProjectStatus } from "@/lib/api";
import { useSettings } from "@/lib/settings";

const STATUS_POLL_MS = 3000;

export default function Dashboard() {
  const { settings } = useSettings();

  if (!settings) return <ConnectCard />;

  return <ProjectList baseUrl={settings.baseUrl} token={settings.token} />;
}

function ProjectList({ baseUrl, token }: { baseUrl: string; token: string }) {
  const config = { baseUrl, token };
  const [projects, setProjects] = useState<Project[] | null>(null);
  const [statuses, setStatuses] = useState<Record<string, ProjectStatus>>({});
  const [pending, setPending] = useState<Record<string, boolean>>({});
  const [error, setError] = useState<string | null>(null);

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

  const groups = useMemo(() => {
    if (!projects) return [];
    const byGroup = new Map<string, Project[]>();
    for (const project of projects) {
      const key = project.group || "";
      const list = byGroup.get(key) ?? [];
      list.push(project);
      byGroup.set(key, list);
    }
    return [...byGroup.entries()];
  }, [projects]);

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

      {!error && projects && projects.length > 0 && (
        <Card className="py-0">
          <CardContent className="flex flex-col gap-0 px-0">
            {groups.map(([group, groupProjects], i) => (
              <div key={group || "__ungrouped"}>
                {i > 0 && <Separator />}
                {group && (
                  <div className="px-4 pt-3 pb-1 text-xs font-medium text-muted-foreground">
                    {group}
                  </div>
                )}
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Projeto</TableHead>
                      <TableHead>Tipo</TableHead>
                      <TableHead>Status</TableHead>
                      <TableHead>Portas</TableHead>
                      <TableHead className="text-right">Ações</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
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
                  </TableBody>
                </Table>
              </div>
            ))}
          </CardContent>
        </Card>
      )}
    </div>
  );
}
