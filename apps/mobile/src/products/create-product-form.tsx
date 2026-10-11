import { ApiResponseError, unwrap } from "@stockroom/api-client";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { TFunction } from "i18next";
import { useRouter } from "expo-router";
import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { ScrollView, StyleSheet, Text, type TextInput } from "react-native";

import { serverFieldErrors } from "@/api/field-errors";
import { useApiClient } from "@/api/provider";
import { Button } from "@/components/button";
import { TextField } from "@/components/text-field";

import { productKey } from "./use-products";
import {
  MaxLengths,
  MaxQuantity,
  newProductRequest,
  validateNewProduct,
  type NewProductField,
  type NewProductValues,
} from "./validate-product";

/** The API's error codes for a SKU or barcode that another product already has. */
const SkuTaken = "sku_taken";
const BarcodeTaken = "barcode_taken";

/**
 * Creates a product (spec 4.1) with a name, and optionally a SKU (generated when left empty), a minimum,
 * a barcode, and the units already on hand, then replaces itself with the new product's screen.
 * `barcode` fills in the barcode, for example one that was scanned and matched no product (spec 5, flow
 * 4).
 */
export function CreateProductForm({
  barcode = "",
}: {
  barcode?: string | undefined;
}) {
  const { t } = useTranslation();
  const client = useApiClient();
  const queryClient = useQueryClient();
  const router = useRouter();

  const [values, setValues] = useState<NewProductValues>({
    name: "",
    sku: "",
    minStock: "",
    barcode,
    initialQuantity: "",
  });
  // Field errors show from the first attempt to submit, not while the user is still typing.
  const [submitted, setSubmitted] = useState(false);
  const skuInput = useRef<TextInput>(null);
  const minStockInput = useRef<TextInput>(null);
  const barcodeInput = useRef<TextInput>(null);
  const initialQuantityInput = useRef<TextInput>(null);

  const create = useMutation({
    mutationFn: (newValues: NewProductValues) =>
      unwrap(
        client.POST("/api/v1/products", {
          body: newProductRequest(newValues),
        }),
      ),
    onSuccess: async (product) => {
      // Lists, Home's figures, and the low-stock list may all include it now.
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["products"] }),
        queryClient.invalidateQueries({ queryKey: ["stats"] }),
      ]);
      queryClient.setQueryData(productKey(product.id), product);
      router.replace({ pathname: "/product/[id]", params: { id: product.id } });
    },
  });

  const clientErrors = validateNewProduct(values);
  const serverErrors = {
    ...serverFieldErrors(create.error, ServerFieldNames),
    ...conflictErrors(create.error, t),
  };
  const fieldError = (field: NewProductField): string | undefined => {
    const clientError = submitted ? clientErrors[field] : undefined;
    return clientError
      ? t(`createProduct.errors.${clientError}`, { max: MaxQuantity })
      : serverErrors[field];
  };
  const formError =
    create.error && Object.keys(serverErrors).length === 0
      ? formErrorText(create.error, t)
      : null;

  const change = (field: NewProductField) => (text: string) => {
    setValues((current) => ({ ...current, [field]: text }));
    // The server's errors were about the previous values.
    if (create.isError) {
      create.reset();
    }
  };

  const submit = () => {
    setSubmitted(true);
    if (Object.keys(clientErrors).length === 0 && !create.isPending) {
      create.mutate(values);
    }
  };

  // Stays busy after success too, until the product's screen replaces the form.
  const busy = create.isPending || create.isSuccess;

  return (
    <ScrollView
      automaticallyAdjustKeyboardInsets
      contentContainerStyle={styles.container}
      contentInsetAdjustmentBehavior="automatic"
      keyboardShouldPersistTaps="handled"
    >
      <TextField
        editable={!busy}
        error={fieldError("name")}
        label={t("createProduct.nameLabel")}
        maxLength={MaxLengths.name}
        onChangeText={change("name")}
        onSubmitEditing={() => skuInput.current?.focus()}
        returnKeyType="next"
        submitBehavior="submit"
        value={values.name}
      />
      <TextField
        ref={skuInput}
        autoCapitalize="characters"
        autoCorrect={false}
        editable={!busy}
        error={fieldError("sku")}
        hint={t("createProduct.skuHint")}
        label={t("createProduct.skuLabel")}
        maxLength={MaxLengths.sku}
        onChangeText={change("sku")}
        onSubmitEditing={() => minStockInput.current?.focus()}
        returnKeyType="next"
        submitBehavior="submit"
        value={values.sku}
      />
      <TextField
        ref={minStockInput}
        editable={!busy}
        error={fieldError("minStock")}
        hint={t("createProduct.minStockHint")}
        keyboardType="number-pad"
        label={t("createProduct.minStockLabel")}
        onChangeText={change("minStock")}
        onSubmitEditing={() => barcodeInput.current?.focus()}
        returnKeyType="next"
        submitBehavior="submit"
        value={values.minStock}
      />
      <TextField
        ref={barcodeInput}
        autoCapitalize="none"
        autoCorrect={false}
        editable={!busy}
        error={fieldError("barcode")}
        label={t("createProduct.barcodeLabel")}
        maxLength={MaxLengths.barcode}
        onChangeText={change("barcode")}
        onSubmitEditing={() => initialQuantityInput.current?.focus()}
        returnKeyType="next"
        submitBehavior="submit"
        value={values.barcode}
      />
      <TextField
        ref={initialQuantityInput}
        editable={!busy}
        error={fieldError("initialQuantity")}
        hint={t("createProduct.initialQuantityHint")}
        keyboardType="number-pad"
        label={t("createProduct.initialQuantityLabel")}
        onChangeText={change("initialQuantity")}
        onSubmitEditing={submit}
        returnKeyType="done"
        value={values.initialQuantity}
      />
      {formError && (
        <Text role="alert" style={styles.error}>
          {formError}
        </Text>
      )}
      <Button
        busy={busy}
        onPress={submit}
        title={busy ? t("createProduct.submitting") : t("createProduct.submit")}
      />
    </ScrollView>
  );
}

/** The API's field names in `validation_failed` details, by form field. */
const ServerFieldNames: Record<string, NewProductField> = {
  name: "name",
  sku: "sku",
  min_stock: "minStock",
  barcode: "barcode",
  initial_quantity: "initialQuantity",
};

/** A SKU or barcode that another product already has, as an error on its field. */
function conflictErrors(
  error: Error | null,
  t: TFunction,
): Partial<Record<NewProductField, string>> {
  if (error instanceof ApiResponseError && error.code === SkuTaken) {
    return { sku: t("createProduct.errors.skuTaken") };
  }
  if (error instanceof ApiResponseError && error.code === BarcodeTaken) {
    return { barcode: t("createProduct.errors.barcodeTaken") };
  }
  return {};
}

/** The error to show for the whole form, when no field shows it. */
function formErrorText(error: Error, t: TFunction): string {
  return error instanceof ApiResponseError
    ? t("createProduct.errors.serverError", { status: String(error.status) })
    : t("createProduct.errors.unreachable");
}

const styles = StyleSheet.create({
  container: {
    gap: 16,
    padding: 24,
  },
  error: {
    color: "#b3261e",
    fontSize: 14,
  },
});
