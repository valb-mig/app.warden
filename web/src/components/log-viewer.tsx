"use client";

import { useEffect, useRef, useState } from "react";
import { Loader2, Terminal } from "lucide-react";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { api, type ApiConfig } from "@/lib/api";

const MAX_LINES = 500;
const ALL_SERVICES = "__all__";

export function LogViewer({ config, projectId }: { config: ApiConfig; projectId: string }) {
  const [services, setServices] = useState<string[]>([]);
  const [selected, setSelected] = useState(ALL_SERVICES);

  useEffect(() => {
    let cancelled = false;
    api
      .services(config, projectId)
      .then((res) => {
        if (!cancelled) setServices(res.services);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.baseUrl, config.token, projectId]);

  return (
    <Card className="flex flex-1 flex-col">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Terminal className="size-4" />
          Logs ao vivo
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-1 flex-col gap-3">
        {services.length > 0 && (
          <Tabs value={selected} onValueChange={setSelected}>
            <TabsList>
              <TabsTrigger value={ALL_SERVICES}>todos</TabsTrigger>
              {services.map((service) => (
                <TabsTrigger key={service} value={service}>
                  {service}
                </TabsTrigger>
              ))}
            </TabsList>
          </Tabs>
        )}
        <LogStream
          key={selected}
          config={config}
          projectId={projectId}
          service={selected === ALL_SERVICES ? undefined : selected}
        />
      </CardContent>
    </Card>
  );
}

function LogStream({
  config,
  projectId,
  service,
}: {
  config: ApiConfig;
  projectId: string;
  service?: string;
}) {
  const [lines, setLines] = useState<string[]>([]);
  const [connected, setConnected] = useState(false);
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    // WS manda o backlog inteiro do ring buffer assim que conecta — não precisa de fetch REST separado.
    const ws = new WebSocket(api.wsUrl(config, projectId, service));
    ws.onopen = () => setConnected(true);
    ws.onmessage = (event) => {
      setLines((prev) => [...prev, event.data as string].slice(-MAX_LINES));
    };

    return () => {
      ws.close();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.baseUrl, config.token, projectId, service]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ block: "end" });
  }, [lines]);

  return (
    <ScrollArea className="h-80 rounded-md border bg-muted/30 p-3">
      {!connected ? (
        <p className="flex items-center gap-2 text-sm text-muted-foreground">
          <Loader2 className="size-3.5 animate-spin" />
          conectando...
        </p>
      ) : lines.length === 0 ? (
        <p className="text-sm text-muted-foreground">sem saída ainda</p>
      ) : (
        <pre className="whitespace-pre-wrap font-mono text-xs leading-relaxed">
          {lines.join("\n")}
        </pre>
      )}
      <div ref={bottomRef} />
    </ScrollArea>
  );
}
