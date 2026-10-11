import type { ReactNode } from "react";

import { ApiProvider } from "@/api/provider";
import { ToastProvider } from "@/components/toast";
import { MovementToastProvider } from "@/stock/movement-toast";
import type { AppStorage } from "@/storage/storage";

/**
 * The app's state around its screens: the server and session with their query cache, and the toast,
 * which confirms stock changes with Undo. Tests render screens inside it as the app does.
 */
export function AppProviders({
  storage,
  children,
}: {
  storage: AppStorage;
  children: ReactNode;
}) {
  return (
    <ApiProvider storage={storage}>
      <ToastProvider>
        <MovementToastProvider>{children}</MovementToastProvider>
      </ToastProvider>
    </ApiProvider>
  );
}
