import { ApiResponseError, type Schema } from "@stockroom/api-client";
import type { TFunction } from "i18next";
import { createContext, use, useCallback, type ReactNode } from "react";
import { useTranslation } from "react-i18next";

import { useToast } from "@/components/toast";

import { insufficientStock, useVoidMovement } from "./use-record-movement";

type Movement = Schema<"StockMovementResponse">;

/** The API's error codes for a movement that cannot be undone. */
const UndoWindowExpired = "undo_window_expired";
const AlreadyVoided = "already_voided";

const ConfirmContext = createContext<((movement: Movement) => void) | null>(
  null,
);

/**
 * Confirms recorded stock movements with a toast that offers Undo (spec 4.2, and spec 5, flows 2 and
 * 3), which voids the movement while the server's undo window is open. It belongs to the app rather
 * than a screen, so Undo works after the screen that recorded the movement has closed, such as the
 * sheet that adds stock. Use it inside a `ToastProvider` and the API's query cache.
 */
export function MovementToastProvider({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const { show } = useToast();
  const { mutate: voidMovement } = useVoidMovement();

  const undo = useCallback(
    function undo(movement: Movement) {
      show({ message: t("movementToast.undoing"), persistent: true });
      voidMovement(movement.id, {
        onSuccess: (reversal) =>
          show({
            message: t("movementToast.undone", {
              name: reversal.product_name,
              quantity: reversal.quantity_after,
            }),
          }),
        onError: (error) =>
          show({
            message: undoErrorText(error, t),
            ...(!(error instanceof ApiResponseError) && {
              action: {
                label: t("common.retry"),
                onPress: () => undo(movement),
              },
            }),
          }),
      });
    },
    [show, t, voidMovement],
  );

  const confirm = useCallback(
    (movement: Movement) =>
      show({
        message: confirmText(movement, t),
        action: {
          label: t("movementToast.undo"),
          onPress: () => undo(movement),
        },
      }),
    [show, t, undo],
  );

  return <ConfirmContext value={confirm}>{children}</ConfirmContext>;
}

/**
 * Shows a toast that confirms the recorded `movement` and offers to undo it. Use it inside a
 * `MovementToastProvider`.
 */
export function useConfirmMovement(): (movement: Movement) => void {
  const confirm = use(ConfirmContext);
  if (!confirm) {
    throw new Error("Use useConfirmMovement inside a MovementToastProvider.");
  }
  return confirm;
}

function confirmText(movement: Movement, t: TFunction): string {
  const values = {
    name: movement.product_name,
    units: Math.abs(movement.delta),
    quantity: movement.quantity_after,
  };
  switch (movement.type) {
    case "receive":
      return t("movementToast.received", values);
    case "issue":
      return t("movementToast.issued", values);
    default:
      return t("movementToast.counted", values);
  }
}

function undoErrorText(error: Error, t: TFunction): string {
  if (!(error instanceof ApiResponseError)) {
    return t("movementToast.errors.undoUnreachable");
  }
  if (error.code === UndoWindowExpired) {
    return t("movementToast.errors.undoWindowExpired");
  }
  if (error.code === AlreadyVoided) {
    return t("movementToast.errors.alreadyUndone");
  }
  if (insufficientStock(error)) {
    return t("movementToast.errors.undoInsufficientStock");
  }
  return t("movementToast.errors.undoServerError", {
    status: String(error.status),
  });
}
