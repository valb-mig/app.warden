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
import { useSettings } from "@/lib/settings";

export function MachineSwitcher() {
  const { machines, activeMachine, setActiveMachineId, addMachine, removeMachine } = useSettings();
  const [open, setOpen] = useState(false);
  const [adding, setAdding] = useState(false);
  const [name, setName] = useState("");
  const [baseUrl, setBaseUrl] = useState("http://127.0.0.1:8420");
  const [token, setToken] = useState("");

  function handleAdd(e: React.FormEvent) {
    e.preventDefault();
    addMachine({ name, baseUrl: baseUrl.replace(/\/$/, ""), token });
    setName("");
    setBaseUrl("http://127.0.0.1:8420");
    setToken("");
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
                className="flex-1 justify-start"
                onClick={() => {
                  setActiveMachineId(m.id);
                  setOpen(false);
                }}
              >
                <Server className="size-4" />
                {m.name}
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
