"use client";

import { useEffect, useState } from "react";
import { FolderCog, FolderSearch, Plus, RefreshCw, Settings2, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { ScrollArea } from "@/components/ui/scroll-area";
import { FolderPicker } from "@/components/folder-picker";
import { ProjectConfigModal } from "@/components/project-config-modal";
import { api, ApiError, type ApiConfig, type DiscoveredProject } from "@/lib/api";
import { useSettings } from "@/lib/settings";

export function SyncDialog() {
  const { settings } = useSettings();
  const [open, setOpen] = useState(false);

  if (!settings) return null;

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={<Button variant="ghost" className="gap-2" />}>
        <RefreshCw className="size-4" />
        Sincronizar
      </DialogTrigger>
      <DialogContent className="max-h-[85vh] overflow-x-hidden overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Descobrir projetos</DialogTitle>
          <DialogDescription>
            Gerencia as pastas onde o Warden procura projetos e mostra o que ainda não foi
            configurado.
          </DialogDescription>
        </DialogHeader>
        {open && <SyncBody apiConfig={{ baseUrl: settings.baseUrl, token: settings.token }} />}
      </DialogContent>
    </Dialog>
  );
}

function SyncBody({ apiConfig }: { apiConfig: ApiConfig }) {
  const [scanPaths, setScanPaths] = useState<string[]>([]);
  const [discovered, setDiscovered] = useState<DiscoveredProject[] | null>(null);
  const [newPath, setNewPath] = useState("");
  const [adding, setAdding] = useState(false);
  const [syncing, setSyncing] = useState(false);

  const sync = async () => {
    setSyncing(true);
    try {
      const res = await api.discover(apiConfig);
      setDiscovered(res.projects);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "falha ao sincronizar");
    } finally {
      setSyncing(false);
    }
  };

  useEffect(() => {
    api
      .getScanPaths(apiConfig)
      .then((res) => {
        setScanPaths(res.scan_paths);
        return sync();
      })
      .catch((err) => {
        toast.error(err instanceof ApiError ? err.message : "falha ao carregar paths");
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function handleAdd(e: React.FormEvent) {
    e.preventDefault();
    setAdding(true);
    try {
      const res = await api.addScanPath(apiConfig, newPath);
      setScanPaths(res.scan_paths);
      setNewPath("");
      await sync();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "path inválido");
    } finally {
      setAdding(false);
    }
  }

  async function handleRemove(path: string) {
    try {
      const res = await api.removeScanPath(apiConfig, path);
      setScanPaths(res.scan_paths);
      await sync();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "falha ao remover path");
    }
  }

  return (
    <div className="flex min-w-0 flex-col gap-4">
      <div className="flex flex-col gap-1.5">
        <p className="text-sm font-medium">Pastas de projetos</p>
        {scanPaths.length === 0 ? (
          <p className="text-sm text-muted-foreground">nenhuma pasta configurada ainda</p>
        ) : (
          <ScrollArea className="max-h-32 rounded-md border">
            <div className="flex flex-col gap-1 p-1">
              {scanPaths.map((path) => (
                <div key={path} className="flex items-center gap-2">
                  <span className="min-w-0 flex-1 truncate font-mono text-xs text-muted-foreground">
                    {path}
                  </span>
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`Remover ${path}`}
                    onClick={() => handleRemove(path)}
                  >
                    <Trash2 className="size-4" />
                  </Button>
                </div>
              ))}
            </div>
          </ScrollArea>
        )}
        <form className="flex gap-2" onSubmit={handleAdd}>
          <Input
            value={newPath}
            onChange={(e) => setNewPath(e.target.value)}
            placeholder="/home/valb/Projects"
            required
          />
          <FolderPicker
            apiConfig={apiConfig}
            trigger={<Button type="button" variant="outline" />}
            onSelect={setNewPath}
          >
            <FolderCog className="size-4" />
          </FolderPicker>
          <Button type="submit" variant="outline" disabled={adding}>
            <Plus className="size-4" />
            Adicionar
          </Button>
        </form>
      </div>

      <div className="flex flex-col gap-1.5">
        <div className="flex items-center justify-between">
          <p className="text-sm font-medium">Projetos descobertos</p>
          <Button variant="ghost" size="sm" disabled={syncing} onClick={() => sync()}>
            <RefreshCw className={`size-3.5 ${syncing ? "animate-spin" : ""}`} />
            ressincronizar
          </Button>
        </div>

        {discovered === null ? (
          <p className="text-sm text-muted-foreground">sincronizando...</p>
        ) : discovered.length === 0 ? (
          <p className="flex items-center gap-2 text-sm text-muted-foreground">
            <FolderSearch className="size-3.5" />
            nada novo pra configurar
          </p>
        ) : (
          <ScrollArea className="h-64 rounded-md border">
            <div className="flex flex-col gap-1 p-1">
              {discovered.map((project) => (
                <div
                  key={project.path}
                  className="flex items-center gap-2 rounded-md border px-2 py-1.5"
                >
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm">{project.name}</p>
                    <p className="truncate font-mono text-xs text-muted-foreground">
                      {project.path}
                    </p>
                  </div>
                  <ProjectConfigModal
                    apiConfig={apiConfig}
                    target={{ mode: "create", path: project.path }}
                    trigger={<Button size="sm" variant="outline" />}
                    onSaved={() => sync()}
                  >
                    <Settings2 className="size-3.5" />
                    configurar
                  </ProjectConfigModal>
                </div>
              ))}
            </div>
          </ScrollArea>
        )}
      </div>
    </div>
  );
}
