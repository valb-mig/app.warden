"use client";

import { useEffect, useState } from "react";
import { ArrowUp, Folder, FolderOpen } from "lucide-react";
import { toast } from "sonner";

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
import { ScrollArea } from "@/components/ui/scroll-area";
import { api, ApiError, type ApiConfig, type BrowseEntry, type BrowseResult } from "@/lib/api";

export function FolderPicker({
  apiConfig,
  trigger,
  children,
  onSelect,
}: {
  apiConfig: ApiConfig;
  trigger: React.ReactElement;
  children: React.ReactNode;
  onSelect: (path: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const [path, setPath] = useState<string | null>(null);
  const [parent, setParent] = useState<string | null>(null);
  const [entries, setEntries] = useState<BrowseEntry[]>([]);

  function applyResult(res: BrowseResult) {
    setPath(res.path);
    setParent(res.parent);
    setEntries(res.entries);
  }

  async function navigate(target?: string) {
    setLoading(true);
    try {
      applyResult(await api.browse(apiConfig, target));
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "falha ao abrir pasta");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (!open) return;
    let cancelled = false;

    async function load() {
      setLoading(true);
      try {
        const res = await api.browse(apiConfig);
        if (!cancelled) applyResult(res);
      } catch (err) {
        if (!cancelled) {
          toast.error(err instanceof ApiError ? err.message : "falha ao abrir pasta");
        }
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

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={trigger}>{children}</DialogTrigger>
      <DialogContent className="max-h-[80vh] overflow-x-hidden overflow-y-auto sm:max-w-md">
        <DialogHeader className="min-w-0">
          <DialogTitle>Escolher pasta</DialogTitle>
          <DialogDescription className="min-w-0 truncate font-mono text-xs">
            {path ?? "carregando..."}
          </DialogDescription>
        </DialogHeader>

        <div className="flex min-w-0 flex-col gap-2">
          <Button
            variant="outline"
            size="sm"
            className="w-fit"
            disabled={!parent || loading}
            onClick={() => parent && navigate(parent)}
          >
            <ArrowUp className="size-3.5" />
            subir
          </Button>

          <ScrollArea className="h-64 rounded-md border">
            <div className="flex flex-col gap-0.5 p-1">
              {loading ? (
                <p className="p-2 text-sm text-muted-foreground">carregando...</p>
              ) : entries.length === 0 ? (
                <p className="p-2 text-sm text-muted-foreground">sem subpastas aqui</p>
              ) : (
                entries.map((entry) => (
                  <button
                    key={entry.path}
                    type="button"
                    onClick={() => navigate(entry.path)}
                    className="flex items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm hover:bg-muted"
                  >
                    <Folder className="size-3.5 text-muted-foreground" />
                    {entry.name}
                  </button>
                ))
              )}
            </div>
          </ScrollArea>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            Cancelar
          </Button>
          <Button
            disabled={!path}
            onClick={() => {
              if (path) onSelect(path);
              setOpen(false);
            }}
          >
            <FolderOpen className="size-4" />
            Usar esta pasta
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
