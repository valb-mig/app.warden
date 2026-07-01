"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { ArrowLeft, Loader2, Play, Square } from "lucide-react";
import { toast } from "sonner";

import { ConnectCard } from "@/components/connect-card";
import { HistoryTable } from "@/components/history-table";
import { LogViewer } from "@/components/log-viewer";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { api, ApiError, type ProjectStatus } from "@/lib/api";
import { useSettings } from "@/lib/settings";

const STATUS_POLL_MS = 3000;

function formatUptime(seconds: number | null): string {
  if (seconds == null) return "—";
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return m > 0 ? `${m}m${s}s` : `${s}s`;
}

export default function ProjectDetail() {
  const { settings } = useSettings();
  const params = useParams<{ id: string }>();
  const projectId = params.id;

  if (!settings) return <ConnectCard />;

  return <ProjectDetailContent baseUrl={settings.baseUrl} token={settings.token} projectId={projectId} />;
}

function ProjectDetailContent({
  baseUrl,
  token,
  projectId,
}: {
  baseUrl: string;
  token: string;
  projectId: string;
}) {
  const config = { baseUrl, token };
  const [status, setStatus] = useState<ProjectStatus | null>(null);
  const [pending, setPending] = useState(false);

  const refreshStatus = useCallback(async () => {
    try {
      setStatus(await api.status(config, projectId));
    } catch {
      // erro silencioso no polling — a próxima tentativa cobre uma falha isolada
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [baseUrl, token, projectId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- fetch inicial ao montar
    refreshStatus();
    const interval = setInterval(refreshStatus, STATUS_POLL_MS);
    return () => clearInterval(interval);
  }, [refreshStatus]);

  async function handleToggle() {
    setPending(true);
    try {
      if (status?.running) {
        await api.stop(config, projectId);
        toast.success("stop disparado");
      } else {
        await api.start(config, projectId);
        toast.success("start disparado");
      }
      await refreshStatus();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "ação falhou");
    } finally {
      setPending(false);
    }
  }

  const running = status?.running ?? false;

  return (
    <div className="flex flex-1 flex-col gap-4 p-4">
      <Link href="/" className="flex w-fit items-center gap-1 text-sm text-muted-foreground hover:underline">
        <ArrowLeft className="size-3.5" />
        Voltar
      </Link>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center justify-between text-base">
            <span className="font-mono">{projectId}</span>
            {status ? (
              <Badge variant={running ? "default" : "secondary"}>
                {running ? "rodando" : "parado"}
              </Badge>
            ) : (
              <Skeleton className="h-5 w-16" />
            )}
          </CardTitle>
        </CardHeader>
        <CardContent className="flex flex-wrap items-center gap-6">
          <div className="text-sm text-muted-foreground">
            PID: <span className="text-foreground">{status?.pid ?? "—"}</span>
          </div>
          <div className="text-sm text-muted-foreground">
            Portas: <span className="text-foreground">{status?.ports.join(", ") || "—"}</span>
          </div>
          <div className="text-sm text-muted-foreground">
            Uptime: <span className="text-foreground">{formatUptime(status?.uptime_seconds ?? null)}</span>
          </div>
          <Button
            className="ml-auto"
            variant={running ? "destructive" : "default"}
            disabled={pending || !status}
            onClick={handleToggle}
          >
            {pending ? (
              <Loader2 className="size-4 animate-spin" />
            ) : running ? (
              <Square className="size-4" />
            ) : (
              <Play className="size-4" />
            )}
            {running ? "Parar" : "Iniciar"}
          </Button>
        </CardContent>
      </Card>

      <LogViewer config={config} projectId={projectId} />
      <HistoryTable config={config} projectId={projectId} />
    </div>
  );
}
