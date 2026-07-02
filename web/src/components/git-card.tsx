"use client";

import { useState } from "react";
import {
  ArrowDown,
  ArrowDownToLine,
  ArrowUp,
  ArrowUpFromLine,
  ChevronDown,
  ChevronRight,
  DownloadCloud,
  FileDiff,
  GitBranch,
  GitCommitHorizontal,
  Loader2,
  RefreshCw,
} from "lucide-react";
import { toast } from "sonner";

import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ScrollArea } from "@/components/ui/scroll-area";
import { api, ApiError, type ApiConfig, type GitInfo, type GitVerb } from "@/lib/api";

interface CommandResultState {
  verb: GitVerb;
  output: string;
  exit_code: number;
}

export function GitCard({
  config,
  projectId,
  git,
  onRefresh,
}: {
  config: ApiConfig;
  projectId: string;
  git: GitInfo | null;
  onRefresh: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [pending, setPending] = useState<GitVerb | null>(null);
  const [result, setResult] = useState<CommandResultState | null>(null);

  // Não é repo git (ou ainda não carregou): não renderiza — fase 1 esconde silenciosamente.
  if (git === null) return null;

  const behind = git.behind ?? 0;
  const ahead = git.ahead ?? 0;

  async function run(verb: GitVerb, confirm = false) {
    setPending(verb);
    try {
      const res = await api.gitCommand(config, projectId, verb, confirm);
      if (res.refused) toast.error(res.output || `${verb}: bloqueado`);
      else if (res.ok) toast.success(`${verb}: ${res.output || "concluído"}`);
      else toast.error(`${verb}: saiu com código ${res.exit_code}`);
      if (res.output && (!res.ok || verb === "pull" || verb === "push")) {
        setResult({ verb, output: res.output, exit_code: res.exit_code });
      }
      onRefresh();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : `${verb}: falhou`);
    } finally {
      setPending(null);
    }
  }

  const busy = pending !== null;

  return (
    <>
      <Card>
        <CardHeader>
          <CardTitle className="text-base">
            <button
              type="button"
              onClick={() => setOpen((v) => !v)}
              className="flex w-full items-center gap-2"
            >
              {open ? (
                <ChevronDown className="size-4 text-muted-foreground" />
              ) : (
                <ChevronRight className="size-4 text-muted-foreground" />
              )}
              <GitBranch className="size-4" />
              <span className="font-mono">{git.branch}</span>

              <span className="ml-auto flex items-center gap-1.5">
                {behind > 0 && (
                  <Badge variant="destructive" title="commits no origin que você ainda não puxou">
                    <ArrowDown className="size-3" />
                    {behind}
                  </Badge>
                )}
                {ahead > 0 && (
                  <Badge variant="outline" title="commits locais ainda não enviados">
                    <ArrowUp className="size-3" />
                    {ahead}
                  </Badge>
                )}
                {git.dirty ? (
                  <Badge variant="secondary" title="arquivos modificados não commitados">
                    <FileDiff className="size-3" />
                    {git.dirty_count}
                  </Badge>
                ) : (
                  <Badge variant="outline">limpo</Badge>
                )}
              </span>
            </button>
          </CardTitle>
        </CardHeader>

        <CardContent className="flex flex-col gap-3 text-sm">
          {git.has_remote ? (
            <div className="flex flex-wrap gap-2">
              <Button size="sm" variant="default" disabled={busy} onClick={() => run("sync")}>
                {pending === "sync" ? (
                  <Loader2 className="size-4 animate-spin" />
                ) : (
                  <RefreshCw className="size-4" />
                )}
                Sync
              </Button>
              <Button size="sm" variant="outline" disabled={busy} onClick={() => run("fetch")}>
                {pending === "fetch" ? (
                  <Loader2 className="size-4 animate-spin" />
                ) : (
                  <DownloadCloud className="size-4" />
                )}
                Fetch
              </Button>

              <ConfirmButton
                verb="pull"
                icon={<ArrowDownToLine className="size-4" />}
                pending={pending === "pull"}
                disabled={busy}
                onConfirm={() => run("pull", true)}
                description="Puxa commits do origin (fast-forward). Recusa se o working tree estiver sujo."
              />
              <ConfirmButton
                verb="push"
                icon={<ArrowUpFromLine className="size-4" />}
                pending={pending === "push"}
                disabled={busy}
                onConfirm={() => run("push", true)}
                description="Envia commits locais pro origin. Pode falhar se faltar credencial na máquina."
              />
            </div>
          ) : (
            <p className="text-xs text-muted-foreground">sem remote configurado</p>
          )}

          {open &&
            (git.last_commit ? (
              <div className="flex items-start gap-2">
                <GitCommitHorizontal className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
                <div className="min-w-0">
                  <p className="truncate">{git.last_commit.subject}</p>
                  <p className="text-xs text-muted-foreground">
                    <span className="font-mono">{git.last_commit.hash}</span> ·{" "}
                    {git.last_commit.author} · {git.last_commit.relative}
                  </p>
                </div>
              </div>
            ) : (
              <p className="text-muted-foreground">sem commits ainda</p>
            ))}
        </CardContent>
      </Card>

      <Dialog open={result !== null} onOpenChange={(o) => !o && setResult(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              git {result?.verb} — exit {result?.exit_code}
            </DialogTitle>
          </DialogHeader>
          <ScrollArea className="h-64 rounded-md border bg-muted/30 p-3">
            <pre className="whitespace-pre-wrap font-mono text-xs leading-relaxed">
              {result?.output || "(sem saída)"}
            </pre>
          </ScrollArea>
        </DialogContent>
      </Dialog>
    </>
  );
}

function ConfirmButton({
  verb,
  icon,
  pending,
  disabled,
  onConfirm,
  description,
}: {
  verb: GitVerb;
  icon: React.ReactNode;
  pending: boolean;
  disabled: boolean;
  onConfirm: () => void;
  description: string;
}) {
  const [open, setOpen] = useState(false);
  return (
    <AlertDialog open={open} onOpenChange={setOpen}>
      <AlertDialogTrigger
        render={<Button variant="outline" size="sm" disabled={disabled} />}
      >
        {pending ? <Loader2 className="size-4 animate-spin" /> : icon}
        {verb}
      </AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Rodar git {verb}?</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancelar</AlertDialogCancel>
          <AlertDialogAction onClick={onConfirm}>Rodar</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
