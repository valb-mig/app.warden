"use client";

import { useEffect, useState } from "react";
import { Loader2, Wrench } from "lucide-react";
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
import { api, ApiError, type Action, type ApiConfig } from "@/lib/api";

interface ActionResultState {
  name: string;
  exit_code: number;
  output: string;
}

export function ActionsCard({ config, projectId }: { config: ApiConfig; projectId: string }) {
  const [actions, setActions] = useState<Action[]>([]);
  const [openConfirm, setOpenConfirm] = useState<string | null>(null);
  const [pending, setPending] = useState<string | null>(null);
  const [result, setResult] = useState<ActionResultState | null>(null);

  useEffect(() => {
    let cancelled = false;
    api
      .listActions(config, projectId)
      .then((res) => {
        if (!cancelled) setActions(res);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.baseUrl, config.token, projectId]);

  async function handleRun(name: string) {
    setOpenConfirm(null);
    setPending(name);
    try {
      const res = await api.runAction(config, projectId, name);
      setResult({ name, ...res });
      if (res.exit_code === 0) toast.success(`${name}: concluído`);
      else toast.error(`${name}: saiu com código ${res.exit_code}`);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : `${name}: ação falhou`);
    } finally {
      setPending(null);
    }
  }

  if (actions.length === 0) return null;

  return (
    <>
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Wrench className="size-4" />
            Ações
          </CardTitle>
        </CardHeader>
        <CardContent className="flex flex-wrap gap-2">
          {actions.map((action) =>
            action.interactive ? (
              <Badge key={action.name} variant="outline" title="Só via terminal, não suportado pela API">
                {action.name} (terminal)
              </Badge>
            ) : (
              <AlertDialog
                key={action.name}
                open={openConfirm === action.name}
                onOpenChange={(open) => setOpenConfirm(open ? action.name : null)}
              >
                <AlertDialogTrigger
                  render={<Button variant="outline" size="sm" disabled={pending === action.name} />}
                >
                  {pending === action.name && <Loader2 className="size-4 animate-spin" />}
                  {action.name}
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>Rodar {action.name}?</AlertDialogTitle>
                    <AlertDialogDescription>
                      Executa o comando configurado no projeto — pode alterar dados (ex:
                      migration, seed).
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>Cancelar</AlertDialogCancel>
                    <AlertDialogAction onClick={() => handleRun(action.name)}>
                      Rodar
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            ),
          )}
        </CardContent>
      </Card>

      <Dialog open={result !== null} onOpenChange={(open) => !open && setResult(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              {result?.name} — exit {result?.exit_code}
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
