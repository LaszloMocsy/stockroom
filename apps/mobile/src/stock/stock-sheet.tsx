import { ApiResponseError, type Schema } from "@stockroom/api-client";
import type { TFunction } from "i18next";
import { useRouter } from "expo-router";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from "react-native";

import { serverFieldErrors } from "@/api/field-errors";
import { Button } from "@/components/button";
import { LoadError } from "@/components/load-error";
import { TextField } from "@/components/text-field";
import { MaxQuantity } from "@/products/validate-product";
import { useProduct } from "@/products/use-products";

import { quantityError } from "./quantity";
import { QuantityStepper } from "./quantity-stepper";
import { ReasonPicker, type Reason } from "./reason-picker";
import {
  insufficientStock,
  quantityChanged,
  useRecordMovement,
  type NewMovement,
} from "./use-record-movement";

/**
 * A movement that adds stock (`receive`), removes it (`issue`), or sets it to a counted quantity
 * (`adjust`).
 */
export type StockSheetType = "receive" | "issue" | "adjust";

/** Why stock moves each way; the server's default comes first. */
const Reasons: Record<StockSheetType, readonly Reason[]> = {
  receive: ["purchase", "return", "found", "correction", "other"],
  issue: ["sale", "damaged", "lost", "return", "correction", "other"],
  adjust: ["count", "correction", "damaged", "lost", "found", "other"],
};

/** The server's longest note. */
const MaxNoteLength = 2000;

type Field = "quantity" | "note";

/**
 * The sheet that adds stock to the product `productId`, removes it, or sets it to a count (spec 4.2,
 * and spec 5, flows 2, 3, and 5): how many, why, and an optional note. Saving records a `receive`, an
 * `issue`, or an `adjust` movement and closes the sheet; the product then shows its new quantity.
 * Saving again after a failure retries with the same idempotency key, so a request that did reach the
 * server is not recorded twice.
 *
 * Removing more than is on hand is refused by the server, unless it allows negative stock, so the
 * sheet does not check it: it shows the server's answer next to the quantity, and stays open.
 *
 * A count shows the quantity it replaces and the difference before it is saved (spec 3.2, rule 5).
 * It is sent with that quantity as `expected_current`: if the quantity has changed meanwhile, the
 * server records nothing, and the sheet shows the new quantity for the user to check their count
 * against and save again. A count equal to the quantity records nothing either, and closes the sheet.
 */
export function StockSheet({
  productId,
  type,
}: {
  productId: string;
  type: StockSheetType;
}) {
  const { t } = useTranslation();
  const router = useRouter();
  const product = useProduct(productId);
  const record = useRecordMovement();

  const counting = type === "adjust";
  const min = counting ? 0 : 1;
  // A count starts empty, so that the user enters what they counted rather than confirming a number.
  const [quantity, setQuantity] = useState(counting ? "" : "1");
  const reasons = Reasons[type];
  const [reason, setReason] = useState<Reason>(reasons[0]!);
  const [note, setNote] = useState("");
  // The quantity's error shows from the first attempt to save, not while the user is still typing.
  const [submitted, setSubmitted] = useState(false);
  // The quantity a count replaces: what the user saw when the product loaded, or the newer quantity
  // the server answered with. Kept apart from the product, which may load again meanwhile.
  const [counted, setCounted] = useState<number | null>(null);
  if (counted === null && product.data) {
    setCounted(product.data.quantity);
  }

  const clientError = quantityError(quantity, min);
  const conflict = quantityChanged(record.error);
  const shortage = insufficientStock(record.error);
  const serverErrors: Partial<Record<Field, string>> = {
    ...serverFieldErrors(record.error, ServerFieldNames),
    ...(shortage && {
      quantity: t("stockSheet.errors.insufficientStock", {
        count: shortage.available,
      }),
    }),
  };
  const quantityText =
    submitted && clientError
      ? t(`quantity.errors.${clientError}`, { min, max: MaxQuantity })
      : serverErrors.quantity;
  let formError = null;
  if (conflict) {
    formError = t("stockSheet.adjust.quantityChanged", { ...conflict });
  } else if (record.error && Object.keys(serverErrors).length === 0) {
    formError = errorText(record.error, type, t);
  }

  // Stays busy after success too, until the sheet has closed.
  const busy = record.isPending || record.isSuccess;

  const change =
    <T,>(set: (value: T) => void) =>
    (value: T) => {
      set(value);
      // The server's errors were about the previous values.
      if (record.isError) {
        record.reset();
      }
    };

  const save = () => {
    setSubmitted(true);
    if (clientError || busy) {
      return;
    }
    const value = Number(quantity.trim());
    const trimmedNote = note.trim();
    const movement: NewMovement = {
      product_id: productId,
      type,
      ...(counting
        ? {
            target_quantity: value,
            ...(counted !== null && { expected_current: counted }),
          }
        : { quantity: value }),
      reason,
      ...(trimmedNote && { note: trimmedNote }),
    };
    record.mutate(movement, {
      // Also when a count recorded nothing, because it matched the quantity.
      onSuccess: () => router.back(),
      onError: (error) => {
        const changed = quantityChanged(error);
        if (changed) {
          setCounted(changed.current);
        }
      },
    });
  };

  let summary;
  if (product.data) {
    summary = (
      <ProductSummary
        product={product.data}
        quantity={
          counting ? (counted ?? product.data.quantity) : product.data.quantity
        }
      />
    );
  } else if (
    product.error instanceof ApiResponseError &&
    product.error.status === 404
  ) {
    return (
      <View style={styles.container}>
        <Text role="alert" style={styles.text}>
          {t("product.notFound")}
        </Text>
        <Button
          onPress={() => router.back()}
          title={t("common.cancel")}
          variant="secondary"
        />
      </View>
    );
  } else if (product.error) {
    summary = (
      <LoadError
        message={t("product.loadFailed")}
        retry={() => void product.refetch()}
        retrying={product.isFetching}
      />
    );
  } else {
    summary = <ActivityIndicator />;
  }

  return (
    <ScrollView
      automaticallyAdjustKeyboardInsets
      contentContainerStyle={styles.container}
      keyboardShouldPersistTaps="handled"
    >
      <Text role="heading" style={styles.title}>
        {t(`stockSheet.${type}.title`)}
      </Text>
      {summary}
      <QuantityStepper
        editable={!busy}
        error={quantityText}
        label={t(`stockSheet.${type}.quantityLabel`)}
        min={min}
        onChangeText={change(setQuantity)}
        // Closes the keyboard, which would otherwise cover Save; the reason and note are optional.
        returnKeyType="done"
        submitBehavior="blurAndSubmit"
        value={quantity}
      />
      {counting && counted !== null && !clientError && (
        <CountChange count={Number(quantity.trim())} previous={counted} />
      )}
      <ReasonPicker
        disabled={busy}
        label={t("stockSheet.reasonLabel")}
        onChange={change(setReason)}
        reasons={reasons}
        value={reason}
      />
      <TextField
        editable={!busy}
        error={serverErrors.note}
        label={t("stockSheet.noteLabel")}
        maxLength={MaxNoteLength}
        multiline
        onChangeText={change(setNote)}
        value={note}
      />
      {formError && (
        <Text role="alert" style={styles.error}>
          {formError}
        </Text>
      )}
      <Button
        busy={busy}
        onPress={save}
        title={busy ? t("stockSheet.submitting") : t("stockSheet.submit")}
      />
      <Button
        onPress={() => router.back()}
        title={t("common.cancel")}
        variant="secondary"
      />
    </ScrollView>
  );
}

/** The product's name and SKU, and `quantity`, how many are on hand. */
function ProductSummary({
  product,
  quantity,
}: {
  product: Schema<"ProductResponse">;
  quantity: number;
}) {
  const { t } = useTranslation();
  return (
    <View accessible style={styles.summary}>
      <Text style={styles.name}>{product.name}</Text>
      <Text style={styles.text}>
        {t("stockSheet.onHand", { sku: product.sku, quantity })}
      </Text>
    </View>
  );
}

/** What a count of `count` changes: the `previous` quantity it replaces, and the difference. */
function CountChange({ previous, count }: { previous: number; count: number }) {
  const { t } = useTranslation();
  const delta = count - previous;
  const amount = { value: Math.abs(delta) };
  return (
    <Text accessibilityLiveRegion="polite" style={styles.text}>
      {delta === 0
        ? t("stockSheet.adjust.noChange", { previous })
        : t("stockSheet.adjust.change", {
            previous,
            count,
            delta:
              delta < 0
                ? t("movement.deltaOut", amount)
                : t("movement.deltaIn", amount),
          })}
    </Text>
  );
}

/** The API's field names in `validation_failed` details, by form field. */
const ServerFieldNames: Record<string, Field> = {
  quantity: "quantity",
  note: "note",
};

/** The error to show for the whole form, when no field shows it. */
function errorText(error: Error, type: StockSheetType, t: TFunction): string {
  if (!(error instanceof ApiResponseError)) {
    return t("stockSheet.errors.unreachable");
  }
  if (error.status === 404) {
    return t("stockSheet.errors.notFound");
  }
  return t(`stockSheet.${type}.serverError`, { status: String(error.status) });
}

const styles = StyleSheet.create({
  container: {
    gap: 16,
    padding: 24,
  },
  title: {
    fontSize: 20,
    fontWeight: "600",
  },
  summary: {
    gap: 2,
  },
  name: {
    fontSize: 16,
    fontWeight: "600",
  },
  text: {
    fontSize: 16,
  },
  error: {
    color: "#b3261e",
    fontSize: 14,
  },
});
