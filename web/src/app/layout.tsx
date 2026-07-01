import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";

import { Header } from "@/components/header";
import { Toaster } from "@/components/ui/sonner";
import { TooltipProvider } from "@/components/ui/tooltip";
import { SettingsProvider } from "@/lib/settings";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Warden",
  description: "Hub local de supervisão de processos e containers",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="pt-BR"
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      <body className="min-h-full flex flex-col">
        <SettingsProvider>
          <TooltipProvider>
            <Header />
            <main className="flex flex-1 flex-col">{children}</main>
            <Toaster />
          </TooltipProvider>
        </SettingsProvider>
      </body>
    </html>
  );
}
