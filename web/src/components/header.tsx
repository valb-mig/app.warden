import Link from "next/link";
import { ShieldHalf } from "lucide-react";

import { SettingsSheet } from "@/components/settings-sheet";

export function Header() {
  return (
    <header className="flex items-center justify-between border-b px-4 py-3">
      <Link href="/" className="flex items-center gap-2 font-semibold">
        <ShieldHalf className="size-5" />
        Warden
      </Link>
      <SettingsSheet />
    </header>
  );
}
