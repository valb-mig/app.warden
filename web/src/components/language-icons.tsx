"use client";

import { useEffect, useState } from "react";
import { FileCode } from "lucide-react";
import {
  SiC,
  SiCplusplus,
  SiGo,
  SiJavascript,
  SiKotlin,
  SiPhp,
  SiPython,
  SiRuby,
  SiRust,
  SiSharp,
  SiTypescript,
  SiVuedotjs,
} from "react-icons/si";
import type { IconType } from "react-icons";

import { api, type ApiConfig } from "@/lib/api";

const ICONS: Record<string, { icon: IconType; color?: string }> = {
  python: { icon: SiPython, color: "#3776AB" },
  javascript: { icon: SiJavascript, color: "#F7DF1E" },
  typescript: { icon: SiTypescript, color: "#3178C6" },
  php: { icon: SiPhp, color: "#777BB4" },
  go: { icon: SiGo, color: "#00ADD8" },
  rust: { icon: SiRust, color: "#CE422B" },
  ruby: { icon: SiRuby, color: "#CC342D" },
  java: { icon: FileCode, color: "#ED8B00" },
  kotlin: { icon: SiKotlin, color: "#7F52FF" },
  c: { icon: SiC, color: "#A8B9CC" },
  cpp: { icon: SiCplusplus, color: "#00599C" },
  csharp: { icon: SiSharp, color: "#239120" },
  vue: { icon: SiVuedotjs, color: "#4FC08D" },
  shell: { icon: FileCode, color: "#4EAA25" },
};

export function LanguageIcons({ config, projectId }: { config: ApiConfig; projectId: string }) {
  const [languages, setLanguages] = useState<string[]>([]);

  useEffect(() => {
    let cancelled = false;
    api
      .languages(config, projectId)
      .then((res) => {
        if (!cancelled) setLanguages(res.languages);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.baseUrl, config.token, projectId]);

  if (languages.length === 0) return null;

  return (
    <span className="flex items-center gap-1.5">
      {languages.map((lang) => {
        const entry = ICONS[lang] ?? { icon: FileCode, color: undefined };
        const Icon = entry.icon;
        return (
          <span key={lang} title={lang}>
            <Icon className="size-3.5" style={entry.color ? { color: entry.color } : undefined} />
          </span>
        );
      })}
    </span>
  );
}
