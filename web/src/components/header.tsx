import Link from "next/link";
import { Search, ShieldHalf } from "lucide-react";

import { CommandPalette, openCommandPalette } from "@/components/command-palette";
import { MachineSwitcher } from "@/components/machine-switcher";
import { SettingsSheet } from "@/components/settings-sheet";
import { SyncDialog } from "@/components/sync-dialog";
import { ThemeToggle } from "@/components/theme-toggle";
import { Button } from "@/components/ui/button";

export function Header() {
  return (
    <header className="flex items-center justify-between border-b px-4 py-3">
      <Link href="/" className="flex items-center gap-2 font-semibold">
        <ShieldHalf className="size-5" />
        Warden
      </Link>
      <div className="flex items-center gap-1">
        <Button
          variant="ghost"
          size="icon"
          aria-label="Abrir command palette"
          onClick={openCommandPalette}
        >
          <Search className="size-4" />
        </Button>
        <SyncDialog />
        <MachineSwitcher />
        <ThemeToggle />
        <SettingsSheet />
        <CommandPalette />
      </div>
    </header>
  );
}
