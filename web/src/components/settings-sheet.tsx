"use client";

import { useState } from "react";
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
import { useSettings } from "@/lib/settings";

export function SettingsSheet() {
  const { settings, setSettings, clear } = useSettings();
  const [baseUrl, setBaseUrl] = useState(settings?.baseUrl ?? "");
  const [token, setToken] = useState(settings?.token ?? "");
  const [open, setOpen] = useState(false);

  function handleSave() {
    setSettings({ baseUrl: baseUrl.replace(/\/$/, ""), token });
    setOpen(false);
  }

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger render={<Button variant="ghost" size="icon" aria-label="Configurações" />}>
        <SettingsIcon className="size-4" />
      </SheetTrigger>
      <SheetContent>
        <SheetHeader>
          <SheetTitle>Conexão com o Warden</SheetTitle>
          <SheetDescription>Atualiza URL da API e token sem perder o histórico.</SheetDescription>
        </SheetHeader>
        <div className="flex flex-col gap-4 px-4">
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
          <Button variant="outline" onClick={clear}>
            Desconectar
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}
