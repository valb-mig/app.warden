"use client";

import { useState } from "react";
import { Plus, Server, Trash2 } from "lucide-react";

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
import { Label } from "@/components/ui/label";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { type BackendKind, useSettings } from "@/lib/settings";

export function MachineSwitcher() {
  const { machines, activeMachine, setActiveMachineId, addMachine, removeMachine } = useSettings();
  const [open, setOpen] = useState(false);
  const [adding, setAdding] = useState(false);
  const [name, setName] = useState("");
  const [baseUrl, setBaseUrl] = useState("http://127.0.0.1:8420");
  const [token, setToken] = useState("");
  const [kind, setKind] = useState<BackendKind>("python");

  function handleAdd(e: React.FormEvent) {
    e.preventDefault();
    addMachine({ name, baseUrl: baseUrl.replace(/\/$/, ""), token, kind });
    setName("");
    setBaseUrl("http://127.0.0.1:8420");
    setToken("");
    setKind("python");
    setAdding(false);
    setOpen(false);
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) setAdding(false);
      }}
    >
      <DialogTrigger render={<Button variant="ghost" className="gap-2" />}>
        <Server className="size-4" />
        {activeMachine ? activeMachine.name : "Conectar"}
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Máquinas</DialogTitle>
          <DialogDescription>Troca entre as conexões salvas ou adiciona uma nova.</DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-1">
          {machines.length === 0 && (
            <p className="text-sm text-muted-foreground">Nenhuma máquina conectada ainda.</p>
          )}
          {machines.map((m) => (
            <div key={m.id} className="flex items-center gap-2">
              <Button
                variant={m.id === activeMachine?.id ? "default" : "outline"}
                className="flex-1 justify-start gap-2"
                onClick={() => {
                  setActiveMachineId(m.id);
                  setOpen(false);
                }}
              >
                <Server className="size-4" />
                {m.name}
                {m.kind === "agent" && (
                  <span className="rounded bg-muted px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
                    Agent
                  </span>
                )}
              </Button>
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label={`Remover ${m.name}`}
                onClick={() => removeMachine(m.id)}
              >
                <Trash2 className="size-4" />
              </Button>
            </div>
          ))}
        </div>

        {adding ? (
          <form className="flex flex-col gap-3" onSubmit={handleAdd}>
            <div className="flex flex-col gap-2">
              <Label>Backend</Label>
              <Tabs value={kind} onValueChange={(v) => setKind(v as BackendKind)}>
                <TabsList>
                  <TabsTrigger value="python">Python (engine atual)</TabsTrigger>
                  <TabsTrigger value="agent">Agent (C#, em teste)</TabsTrigger>
                </TabsList>
              </Tabs>
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="switcher-name">Nome da máquina</Label>
              <Input
                id="switcher-name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Trabalho, Casa, Faculdade..."
                required
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="switcher-base-url">URL da API</Label>
              <Input
                id="switcher-base-url"
                value={baseUrl}
                onChange={(e) => setBaseUrl(e.target.value)}
                placeholder="http://127.0.0.1:8420"
                required
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="switcher-token">Token</Label>
              <Input
                id="switcher-token"
                type="password"
                value={token}
                onChange={(e) => setToken(e.target.value)}
                placeholder="conteúdo de ~/.warden/api_token"
                required
              />
            </div>
            <div className="flex gap-2">
              <Button type="submit">Adicionar</Button>
              <Button type="button" variant="outline" onClick={() => setAdding(false)}>
                Cancelar
              </Button>
            </div>
          </form>
        ) : (
          <Button variant="outline" onClick={() => setAdding(true)}>
            <Plus className="size-4" />
            Nova máquina
          </Button>
        )}
      </DialogContent>
    </Dialog>
  );
}
