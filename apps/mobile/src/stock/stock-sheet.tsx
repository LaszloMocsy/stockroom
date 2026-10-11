import { ApiResponseError, type Schema } from "@stockroom/api-client";
import type { TFunction } from "i18next";
import { useRouter } from "expo-router";
import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  ScrollView,
  StyleSheet,
  Text,
  View,
  type TextInput,
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
import { insufficientStock, useRecordMovement } from "./use-record-movement";

/** A movement that adds stock (`receive`) or removes it (`issue`). */
export type StockSheetType = "receive" | "issue";

/** Why stock moves each way; the server's default comes first. */
const Reasons: Record<StockSheetType, readonly Reason[]> = {
  receive: ["purchase", "return", "found", "correction", "other"],
  issue: ["sale", "damaged", "lost", "return", "correction", "other"],
};

/** The server's longest note. */
const MaxNoteLength = 2000;

type Field = "quantity" | "note";

/**
 * The sheet that adds stock to the product `productId` or removes it (spec 4.2, and spec 5, flows 2
 * and 3): how many, one by default, why, and an optional note. Saving records a `receive` or an
 * `issue` movement and closes the sheet; the product then shows its new quantity. Saving again after a
 * failure retries with the same idempotency key, so a request that did reach the server is not
 * recorded twice.
 *
 * Removing more than is on hand is refused by the server, unless it allows negative stock, so the
 * sheet does not check it: it shows the server's answer next to the quantity, and stays open.
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

  const [quantity, setQuantity] = useState("1");
  const reasons = Reasons[type];
  const [reason, setReason] = useState<Reason>(reasons[0]!);
  const [note, setNote] = useState("");
  // The quantity's error shows from the first attempt to save, not while the user is still typing.
  const [submitted, setSubmitted] = useState(false);
  const noteInput = useRef<TextInput>(null);

  const clientError = quantityError(quantity, 1);
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
      ? t(`quantity.errors.${clientError}`, { min: 1, max: MaxQuantity })
      : serverErrors.quantity;
  const formError =
    record.error && Object.keys(serverErrors).length === 0
      ? errorText(record.error, type, t)
      : null;

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
    const trimmedNote = note.trim();
    record.mutate(
      {
        product_id: productId,
        type,
        quantity: Number(quantity.trim()),
        reason,
        ...(trimmedNote && { note: trimmedNote }),
      },
      { onSuccess: () => router.back() },
    );
  };

  let summary;
  if (product.data) {
    summary = <ProductSummary product={product.data} />;
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
        min={1}
        onChangeText={change(setQuantity)}
        onSubmitEditing={() => noteInput.current?.focus()}
        returnKeyType="next"
        submitBehavior="submit"
        value={quantity}
      />
      <ReasonPicker
        disabled={busy}
        label={t("stockSheet.reasonLabel")}
        onChange={change(setReason)}
        reasons={reasons}
        value={reason}
      />
      <TextField
        ref={noteInput}
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

/** The product's name and SKU, and how many are on hand now. */
function ProductSummary({ product }: { product: Schema<"ProductResponse"> }) {
  const { t } = useTranslation();
  return (
    <View accessible style={styles.summary}>
      <Text style={styles.name}>{product.name}</Text>
      <Text style={styles.text}>
        {t("stockSheet.onHand", {
          sku: product.sku,
          quantity: product.quantity,
        })}
      </Text>
    </View>
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
