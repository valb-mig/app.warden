"use client";

import { useEffect, useState } from "react";
import { Settings as SettingsIcon } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { type BackendKind, useSettings } from "@/lib/settings";

export function SettingsSheet() {
  const { activeMachine, updateMachine, removeMachine } = useSettings();
  const [name, setName] = useState(activeMachine?.name ?? "");
  const [baseUrl, setBaseUrl] = useState(activeMachine?.baseUrl ?? "");
  const [token, setToken] = useState(activeMachine?.token ?? "");
  const [kind, setKind] = useState<BackendKind>(activeMachine?.kind ?? "python");
  const [open, setOpen] = useState(false);

  useEffect(() => {
    if (open) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- reseta o form com os dados atuais da máquina toda vez que o sheet abre
      setName(activeMachine?.name ?? "");
      setBaseUrl(activeMachine?.baseUrl ?? "");
      setToken(activeMachine?.token ?? "");
      setKind(activeMachine?.kind ?? "python");
    }
  }, [open, activeMachine]);

  if (!activeMachine) return null;

  function handleSave() {
    if (!activeMachine) return;
    updateMachine(activeMachine.id, { name, baseUrl: baseUrl.replace(/\/$/, ""), token, kind });
    setOpen(false);
  }

  function handleRemove() {
    if (!activeMachine) return;
    removeMachine(activeMachine.id);
    setOpen(false);
  }

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger render={<Button variant="ghost" size="icon" aria-label="Configurações" />}>
        <SettingsIcon className="size-4" />
      </SheetTrigger>
      <SheetContent>
        <SheetHeader>
          <SheetTitle>Conexão com {activeMachine.name}</SheetTitle>
          <SheetDescription>Atualiza nome, URL da API e token sem perder o histórico.</SheetDescription>
        </SheetHeader>
        <div className="flex flex-col gap-4 px-4">
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
            <Label htmlFor="settings-name">Nome da máquina</Label>
            <Input id="settings-name" value={name} onChange={(e) => setName(e.target.value)} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="settings-base-url">URL da API</Label>
            <Input id="settings-base-url" value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="settings-token">Token</Label>
            <Input
              id="settings-token"
              type="password"
              value={token}
              onChange={(e) => setToken(e.target.value)}
            />
          </div>
        </div>
        <SheetFooter>
          <Button onClick={handleSave}>Salvar</Button>
          <Button variant="outline" onClick={handleRemove}>
            Remover esta máquina
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}
