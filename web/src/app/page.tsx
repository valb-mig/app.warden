"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { ChevronRight, Loader2, Play, RefreshCw, Square } from "lucide-react";
import { toast } from "sonner";

import { ConnectCard } from "@/components/connect-card";
import { LanguageIcons } from "@/components/language-icons";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
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

  if (error) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-3 p-6 text-center">
        <p className="text-muted-foreground">{error}</p>
        <Button variant="outline" onClick={loadProjects}>
          <RefreshCw className="size-4" />
          Tentar de novo
        </Button>
      </div>
    );
  }

  if (!projects) {
    return (
      <div className="flex flex-col gap-3 p-6">
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-10 w-full" />
      </div>
    );
  }

  if (projects.length === 0) {
    return (
      <div className="flex flex-1 items-center justify-center p-6 text-muted-foreground">
        Nenhum projeto configurado em <code className="mx-1">~/.warden/</code>.
      </div>
    );
  }

  return (
    <div className="p-4">
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
          {projects.map((project) => {
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
                  {project.group && (
                    <span className="text-xs text-muted-foreground">{project.group}</span>
                  )}
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
  );
}
