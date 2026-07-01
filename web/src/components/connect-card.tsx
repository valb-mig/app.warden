"use client";

import { useState } from "react";
import { ShieldCheck } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useSettings } from "@/lib/settings";

export function ConnectCard() {
  const { setSettings } = useSettings();
  const [baseUrl, setBaseUrl] = useState("http://127.0.0.1:8420");
  const [token, setToken] = useState("");

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSettings({ baseUrl: baseUrl.replace(/\/$/, ""), token });
  }

  return (
    <div className="flex flex-1 items-center justify-center p-6">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <div className="flex items-center gap-2">
            <ShieldCheck className="size-5" />
            <CardTitle>Conectar ao Warden</CardTitle>
          </div>
          <CardDescription>
            Endereço da API e token ficam em <code>~/.warden/api_token</code> na máquina que roda o
            motor.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={handleSubmit}>
            <div className="flex flex-col gap-2">
              <Label htmlFor="baseUrl">URL da API</Label>
              <Input
                id="baseUrl"
                value={baseUrl}
                onChange={(e) => setBaseUrl(e.target.value)}
                placeholder="http://127.0.0.1:8420"
                required
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="token">Token</Label>
              <Input
                id="token"
                type="password"
                value={token}
                onChange={(e) => setToken(e.target.value)}
                placeholder="conteúdo de ~/.warden/api_token"
                required
              />
            </div>
            <Button type="submit">Conectar</Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
