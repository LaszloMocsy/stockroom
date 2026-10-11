import { ApiResponseError } from "@stockroom/api-client";
import type { TFunction } from "i18next";
import { useTranslation } from "react-i18next";
import { Pressable, StyleSheet, Text } from "react-native";

import { useToast } from "@/components/toast";

import { useConfirmMovement } from "./movement-toast";
import { insufficientStock, useRecordMovement } from "./use-record-movement";

type QuickType = "receive" | "issue";

/**
 * Adds or removes one unit of the product `productId` at a time (spec 4.2), each confirmed by a toast
 * with Undo (see `MovementToastProvider`). A failure shows a toast too; sending again after a dropped
 * request reuses its idempotency key, so it is not counted twice.
 *
 * Only the last of several quick taps shows its toast, so Undo reverses the last one.
 */
export function useQuickStock(productId: string) {
  const { t } = useTranslation();
  const { show } = useToast();
  const confirm = useConfirmMovement();
  const record = useRecordMovement();

  const move = (type: QuickType) => {
    record.mutate(
      { product_id: productId, type, quantity: 1 },
      {
        onSuccess: (movement) => movement && confirm(movement),
        onError: (error) =>
          show({
            message: moveErrorText(error, type, t),
            ...(!(error instanceof ApiResponseError) && {
              action: { label: t("common.retry"), onPress: () => move(type) },
            }),
          }),
      },
    );
  };

  return { add: () => move("receive"), remove: () => move("issue") };
}

/** The "−1" and "+1" buttons, named "Remove one" and "Add one" for screen readers. */
export function QuickStockButtons({
  add,
  remove,
}: {
  add: () => void;
  remove: () => void;
}) {
  const { t } = useTranslation();
  return (
    <>
      <QuickButton label={t("quickStock.issue.label")} onPress={remove}>
        −1
      </QuickButton>
      <QuickButton label={t("quickStock.receive.label")} onPress={add}>
        +1
      </QuickButton>
    </>
  );
}

function QuickButton({
  label,
  onPress,
  children,
}: {
  label: string;
  onPress: () => void;
  children: string;
}) {
  return (
    <Pressable
      accessibilityLabel={label}
      onPress={onPress}
      role="button"
      style={({ pressed }) => [styles.button, pressed && styles.pressed]}
    >
      <Text style={styles.text}>{children}</Text>
    </Pressable>
  );
}

function moveErrorText(error: Error, type: QuickType, t: TFunction): string {
  const shortage = insufficientStock(error);
  if (shortage) {
    return t("stockSheet.errors.insufficientStock", {
      count: shortage.available,
    });
  }
  if (!(error instanceof ApiResponseError)) {
    return t(`quickStock.${type}.unreachable`);
  }
  if (error.status === 404) {
    return t("stockSheet.errors.notFound");
  }
  return t("quickStock.errors.serverError", { status: String(error.status) });
}

const styles = StyleSheet.create({
  button: {
    width: 64,
    minHeight: 56,
    alignItems: "center",
    justifyContent: "center",
    borderRadius: 8,
    borderWidth: 2,
    borderColor: "#0a5cc2",
  },
  pressed: {
    opacity: 0.6,
  },
  text: {
    color: "#0a5cc2",
    fontSize: 22,
    fontWeight: "600",
    fontVariant: ["tabular-nums"],
  },
});
