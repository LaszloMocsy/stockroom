import type { Schema } from "@stockroom/api-client";
import { useTranslation } from "react-i18next";
import { StyleSheet, Text, View } from "react-native";

export interface MovementRowProps {
  movement: Schema<"StockMovementResponse">;
  /** Whether a later movement voided this one. */
  voided: boolean;
}

/**
 * One entry of the stock ledger (spec 3.1): what happened and by how much, why, who did it, when, and
 * how many were on hand after it, with its note.
 */
export function MovementRow({ movement, voided }: MovementRowProps) {
  const { t } = useTranslation();
  const amount = { value: Math.abs(movement.delta) };
  return (
    // One element for screen readers, read top to bottom.
    <View accessible style={styles.row}>
      <View style={styles.headline}>
        <Text style={styles.type}>
          {t(`movement.types.${movement.type}`)}
          {voided && ` ${t("movement.voided")}`}
        </Text>
        <Text style={styles.delta}>
          {movement.delta < 0
            ? t("movement.deltaOut", amount)
            : t("movement.deltaIn", amount)}
        </Text>
      </View>
      <Text style={styles.text}>
        {t("movement.reasonAndActor", {
          reason: t(`movement.reasons.${movement.reason}`),
          actor: movement.actor_name,
        })}
      </Text>
      <Text style={styles.detail}>
        {t("movement.whenAndAfter", {
          when: new Date(movement.created_at),
          quantity: movement.quantity_after,
        })}
      </Text>
      {movement.note && <Text style={styles.note}>{movement.note}</Text>}
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    gap: 2,
    paddingVertical: 10,
  },
  headline: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 12,
  },
  type: {
    flexShrink: 1,
    fontSize: 16,
    fontWeight: "600",
  },
  delta: {
    fontSize: 16,
    fontWeight: "600",
    fontVariant: ["tabular-nums"],
  },
  text: {
    fontSize: 16,
  },
  detail: {
    fontSize: 14,
  },
  note: {
    fontSize: 14,
    fontStyle: "italic",
  },
});
