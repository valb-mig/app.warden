"use client";

import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ScrollArea } from "@/components/ui/scroll-area";
import { api, ApiError, type ApiConfig, type ProjectConfigPayload } from "@/lib/api";

function splitCommand(input: string): string[] {
  const matches = input.match(/(?:[^\s"]+|"[^"]*")+/g) ?? [];
  return matches.map((m) => m.replace(/^"|"$/g, ""));
}

function toggle(set: Set<number>, i: number): Set<number> {
  const next = new Set(set);
  if (next.has(i)) {
    next.delete(i);
  } else {
    next.add(i);
  }
  return next;
}

export type ConfigTarget = { mode: "create"; path: string } | { mode: "edit"; projectId: string };

export function ProjectConfigModal({
  apiConfig,
  target,
  trigger,
  children,
  onSaved,
}: {
  apiConfig: ApiConfig;
  target: ConfigTarget;
  trigger: React.ReactElement;
  children: React.ReactNode;
  onSaved?: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [projectConfig, setProjectConfig] = useState<ProjectConfigPayload | null>(null);
  const [toml, setToml] = useState<string | null>(null);
  const [startCmd, setStartCmd] = useState("");
  const [removedLogSources, setRemovedLogSources] = useState<Set<number>>(new Set());
  const [removedActions, setRemovedActions] = useState<Set<number>>(new Set());

  useEffect(() => {
    if (!open) return;
    let cancelled = false;

    async function load() {
      setLoading(true);
      setProjectConfig(null);
      setToml(null);
      setRemovedLogSources(new Set());
      setRemovedActions(new Set());
      try {
        const cfg =
          target.mode === "create"
            ? await api.previewConfig(apiConfig, target.path).then((res) => {
                if (!cancelled) setToml(res.toml);
                return res.config;
              })
            : await api.getProjectConfig(apiConfig, target.projectId);
        if (cancelled) return;
        setProjectConfig(cfg);
        setStartCmd(cfg.start ? cfg.start.cmd.join(" ") : "");
      } catch (err) {
        if (cancelled) return;
        toast.error(err instanceof ApiError ? err.message : "falha ao carregar config");
        setOpen(false);
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    load();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  async function handleSave() {
    if (!projectConfig) return;
    setSaving(true);
    try {
      const payload: ProjectConfigPayload = {
        ...projectConfig,
        start: projectConfig.start
          ? { ...projectConfig.start, cmd: splitCommand(startCmd) }
          : null,
        log_sources: projectConfig.log_sources.filter((_, i) => !removedLogSources.has(i)),
        actions: projectConfig.actions.filter((_, i) => !removedActions.has(i)),
      };
      await api.applyConfig(apiConfig, payload);
      toast.success(`${payload.id}: config salva`);
      window.dispatchEvent(new Event("warden:project-configured"));
      onSaved?.();
      setOpen(false);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "falha ao salvar config");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={trigger}>{children}</DialogTrigger>
      <DialogContent className="flex max-h-[85vh] flex-col overflow-x-hidden overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Configurar projeto</DialogTitle>
          <DialogDescription>
            {target.mode === "create" ? target.path : `editando ${target.projectId}`}
          </DialogDescription>
        </DialogHeader>

        {loading || !projectConfig ? (
          <p className="flex items-center gap-2 text-sm text-muted-foreground">
            <Loader2 className="size-3.5 animate-spin" />
            detectando...
          </p>
        ) : (
          <div className="flex min-w-0 flex-col gap-4">
            <div className="flex min-w-0 items-center gap-2">
              <Badge variant="outline" className="shrink-0">
                {projectConfig.type}
              </Badge>
              <span className="min-w-0 truncate font-mono text-xs text-muted-foreground">
                {projectConfig.path}
              </span>
            </div>

            <div className="grid grid-cols-2 gap-3">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="cfg-id">id</Label>
                <Input
                  id="cfg-id"
                  value={projectConfig.id}
                  disabled={target.mode === "edit"}
                  onChange={(e) => setProjectConfig({ ...projectConfig, id: e.target.value })}
                />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="cfg-group">grupo</Label>
                <Input
                  id="cfg-group"
                  value={projectConfig.group ?? ""}
                  placeholder="opcional"
                  onChange={(e) =>
                    setProjectConfig({ ...projectConfig, group: e.target.value || null })
                  }
                />
              </div>
            </div>

            {projectConfig.start && (
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="cfg-start">comando de start</Label>
                <Input
                  id="cfg-start"
                  value={startCmd}
                  onChange={(e) => setStartCmd(e.target.value)}
                />
              </div>
            )}

            {projectConfig.log_sources.length > 0 && (
              <div className="flex flex-col gap-1.5">
                <Label>log sources</Label>
                <div className="flex flex-col gap-1">
                  {projectConfig.log_sources.map((source, i) => (
                    <Button
                      key={`${source.name}-${i}`}
                      type="button"
                      size="sm"
                      variant={removedLogSources.has(i) ? "outline" : "default"}
                      className="w-full justify-start overflow-hidden"
                      onClick={() => setRemovedLogSources((s) => toggle(s, i))}
                    >
                      <span className="truncate">
                        {source.name} ({source.type})
                      </span>
                    </Button>
                  ))}
                </div>
              </div>
            )}

            {projectConfig.actions.length > 0 && (
              <div className="flex flex-col gap-1.5">
                <Label>actions</Label>
                <div className="flex flex-col gap-1">
                  {projectConfig.actions.map((action, i) => (
                    <Button
                      key={`${action.name}-${i}`}
                      type="button"
                      size="sm"
                      variant={removedActions.has(i) ? "outline" : "default"}
                      className="w-full justify-start overflow-hidden font-mono"
                      onClick={() => setRemovedActions((s) => toggle(s, i))}
                    >
                      <span className="truncate">
                        {action.name}: {action.cmd.join(" ")}
                      </span>
                    </Button>
                  ))}
                </div>
              </div>
            )}

            {toml && (
              <div className="flex flex-col gap-1.5">
                <Label>preview</Label>
                <ScrollArea className="h-32 rounded-md border bg-muted/30 p-2">
                  <pre className="whitespace-pre-wrap font-mono text-xs">{toml}</pre>
                </ScrollArea>
              </div>
            )}
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            Cancelar
          </Button>
          <Button disabled={!projectConfig || saving} onClick={handleSave}>
            {saving && <Loader2 className="size-4 animate-spin" />}
            Salvar
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
