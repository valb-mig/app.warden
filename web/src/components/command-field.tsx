"use client";

import { cn } from "@/lib/utils";

export function CommandField({
  value,
  onChange,
  id,
  disabled,
  placeholder,
  className,
}: {
  value: string;
  onChange: (value: string) => void;
  id?: string;
  disabled?: boolean;
  placeholder?: string;
  className?: string;
}) {
  return (
    <div
      className={cn(
        "flex items-center gap-2 rounded-md border border-zinc-800 bg-zinc-950 px-2.5 py-1.5 transition-opacity focus-within:border-zinc-600",
        disabled && "opacity-50",
        className
      )}
    >
      <span className="select-none font-mono text-xs text-zinc-500">$</span>
      <input
        id={id}
        value={value}
        disabled={disabled}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
        spellCheck={false}
        autoCapitalize="off"
        autoCorrect="off"
        className="min-w-0 flex-1 bg-transparent font-mono text-sm text-zinc-100 outline-none placeholder:text-zinc-600 disabled:cursor-not-allowed"
      />
    </div>
  );
}
