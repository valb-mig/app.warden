import Link from "next/link";
import { ShieldHalf } from "lucide-react";

import { MachineSwitcher } from "@/components/machine-switcher";
import { SettingsSheet } from "@/components/settings-sheet";
import { ThemeToggle } from "@/components/theme-toggle";

export function Header() {
  return (
    <header className="flex items-center justify-between border-b px-4 py-3">
      <Link href="/" className="flex items-center gap-2 font-semibold">
        <ShieldHalf className="size-5" />
        Warden
      </Link>
      <div className="flex items-center gap-1">
        <MachineSwitcher />
        <ThemeToggle />
        <SettingsSheet />
      </div>
    </header>
  );
}
