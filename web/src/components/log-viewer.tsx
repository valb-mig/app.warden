"use client";

import { useEffect, useRef, useState } from "react";
import { Terminal } from "lucide-react";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ScrollArea } from "@/components/ui/scroll-area";
import { api, type ApiConfig } from "@/lib/api";

const MAX_LINES = 500;

export function LogViewer({ config, projectId }: { config: ApiConfig; projectId: string }) {
  const [lines, setLines] = useState<string[]>([]);
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    // WS manda o backlog inteiro do ring buffer assim que conecta — não precisa de fetch REST separado.
    const ws = new WebSocket(api.wsUrl(config, projectId));
    ws.onmessage = (event) => {
      setLines((prev) => [...prev, event.data as string].slice(-MAX_LINES));
    };

    return () => {
      ws.close();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.baseUrl, config.token, projectId]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ block: "end" });
  }, [lines]);

  return (
    <Card className="flex flex-1 flex-col">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Terminal className="size-4" />
          Logs ao vivo
        </CardTitle>
      </CardHeader>
      <CardContent className="flex-1">
        <ScrollArea className="h-80 rounded-md border bg-muted/30 p-3">
          {lines.length === 0 ? (
            <p className="text-sm text-muted-foreground">sem saída ainda</p>
          ) : (
            <pre className="whitespace-pre-wrap font-mono text-xs leading-relaxed">
              {lines.join("\n")}
            </pre>
          )}
          <div ref={bottomRef} />
        </ScrollArea>
      </CardContent>
    </Card>
  );
}
