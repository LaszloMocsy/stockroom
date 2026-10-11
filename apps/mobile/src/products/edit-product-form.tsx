import { ApiResponseError, unwrap, type Schema } from "@stockroom/api-client";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { TFunction } from "i18next";
import { useRouter } from "expo-router";
import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  Alert,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
  type TextInput,
} from "react-native";

import { serverFieldErrors } from "@/api/field-errors";
import { useApiClient } from "@/api/provider";
import { Button } from "@/components/button";
import { LoadError } from "@/components/load-error";
import { TextField } from "@/components/text-field";
import { useToast } from "@/components/toast";

import { productKey, useProduct } from "./use-products";
import {
  MaxLengths,
  MaxQuantity,
  productEditValues,
  productUpdateRequest,
  validateProductEdit,
  type ProductEditField,
} from "./validate-product";

type Product = Schema<"ProductResponse">;

/**
 * Edits the product `id` (spec 4.1): its name, description, and minimum, and removes its barcodes. The
 * SKU is shown but cannot change. Saving sends only what changed and returns to the product.
 */
export function EditProduct({ id }: { id: string }) {
  const { t } = useTranslation();
  const product = useProduct(id);

  if (product.data) {
    // Mounted once the product has loaded, so that the form starts with its values.
    return <EditProductForm product={product.data} />;
  }
  return (
    <View style={styles.container}>
      {product.error instanceof ApiResponseError &&
      product.error.status === 404 ? (
        <Text role="alert" style={styles.text}>
          {t("product.notFound")}
        </Text>
      ) : product.error ? (
        <LoadError
          message={t("product.loadFailed")}
          retry={() => void product.refetch()}
          retrying={product.isFetching}
        />
      ) : (
        <ActivityIndicator />
      )}
    </View>
  );
}

/**
 * The form for `product`. Its fields keep what the user typed, while the barcodes follow the product
 * as it changes: removing one takes effect at once, after the user confirms it, rather than on Save.
 */
function EditProductForm({ product }: { product: Product }) {
  const { t } = useTranslation();
  const client = useApiClient();
  const queryClient = useQueryClient();
  const router = useRouter();
  const { show } = useToast();

  const [values, setValues] = useState(() => productEditValues(product));
  // Field errors show from the first attempt to save, not while the user is still typing.
  const [submitted, setSubmitted] = useState(false);
  const descriptionInput = useRef<TextInput>(null);

  /** Shows `updated` as the product, and has the lists that may include it load again. */
  const productChanged = (updated: Product) => {
    queryClient.setQueryData(productKey(updated.id), updated);
    void queryClient.invalidateQueries({ queryKey: ["products"] });
  };

  const save = useMutation({
    mutationFn: (changes: Schema<"UpdateProductRequest">) =>
      unwrap(
        client.PATCH("/api/v1/products/{id}", {
          params: { path: { id: product.id } },
          body: changes,
        }),
      ),
    onSuccess: (updated) => {
      productChanged(updated);
      show({ message: t("editProduct.saved", { name: updated.name }) });
      router.back();
    },
  });

  const removeBarcode = useMutation({
    mutationFn: (barcode: string) =>
      unwrap(
        client.DELETE("/api/v1/products/{id}/barcodes/{barcode}", {
          params: { path: { id: product.id, barcode } },
        }),
      ),
    onSuccess: productChanged,
    onError: (error) => {
      // Someone else removed it meanwhile: the product shows its barcodes as they are now.
      if (error instanceof ApiResponseError && error.status === 404) {
        void queryClient.invalidateQueries({
          queryKey: productKey(product.id),
        });
      }
    },
  });

  const clientErrors = validateProductEdit(values);
  const serverErrors = serverFieldErrors(save.error, ServerFieldNames);
  const fieldError = (field: ProductEditField): string | undefined => {
    const clientError = submitted ? clientErrors[field] : undefined;
    return clientError
      ? t(`editProduct.errors.${clientError}`, { max: MaxQuantity })
      : serverErrors[field];
  };
  const formError =
    save.error && Object.keys(serverErrors).length === 0
      ? errorText(save.error, "saveFailed", t)
      : null;

  const change = (field: ProductEditField) => (text: string) => {
    setValues((current) => ({ ...current, [field]: text }));
    // The server's errors were about the previous values.
    if (save.isError) {
      save.reset();
    }
  };

  const submit = () => {
    setSubmitted(true);
    if (Object.keys(clientErrors).length > 0 || save.isPending) {
      return;
    }
    const changes = productUpdateRequest(values, product);
    if (Object.keys(changes).length === 0) {
      router.back();
      return;
    }
    save.mutate(changes);
  };

  const confirmRemoval = (barcode: string) =>
    Alert.alert(
      t("editProduct.removeBarcodeTitle"),
      t("editProduct.removeBarcodeMessage", { barcode, name: product.name }),
      [
        { text: t("common.cancel"), style: "cancel" },
        {
          text: t("editProduct.removeBarcode"),
          style: "destructive",
          onPress: () => removeBarcode.mutate(barcode),
        },
      ],
    );

  // Stays busy after success too, until the product's screen is back in front.
  const busy = save.isPending || save.isSuccess;

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
        label={t("editProduct.nameLabel")}
        maxLength={MaxLengths.name}
        onChangeText={change("name")}
        onSubmitEditing={() => descriptionInput.current?.focus()}
        returnKeyType="next"
        submitBehavior="submit"
        value={values.name}
      />
      <TextField
        ref={descriptionInput}
        editable={!busy}
        error={fieldError("description")}
        label={t("editProduct.descriptionLabel")}
        maxLength={MaxLengths.description}
        multiline
        onChangeText={change("description")}
        value={values.description}
      />
      <TextField
        editable={!busy}
        error={fieldError("minStock")}
        hint={t("editProduct.minStockHint")}
        keyboardType="number-pad"
        label={t("editProduct.minStockLabel")}
        onChangeText={change("minStock")}
        onSubmitEditing={submit}
        returnKeyType="done"
        value={values.minStock}
      />
      {/* One element for screen readers, read as the label and the SKU. */}
      <View accessible style={styles.field}>
        <Text style={styles.label}>{t("editProduct.skuLabel")}</Text>
        <Text style={styles.text}>{product.sku}</Text>
        <Text style={styles.hint}>{t("editProduct.skuHint")}</Text>
      </View>
      <Barcodes
        barcodes={product.barcodes}
        error={
          removeBarcode.error
            ? errorText(removeBarcode.error, "removeBarcodeFailed", t)
            : null
        }
        onRemove={confirmRemoval}
        removing={removeBarcode.isPending ? removeBarcode.variables : null}
      />
      {formError && (
        <Text role="alert" style={styles.error}>
          {formError}
        </Text>
      )}
      <Button
        busy={busy}
        onPress={submit}
        title={busy ? t("editProduct.submitting") : t("editProduct.submit")}
      />
    </ScrollView>
  );
}

/** The product's barcodes, each with a button that removes it; `removing` is the one being removed. */
function Barcodes({
  barcodes,
  onRemove,
  removing,
  error,
}: {
  barcodes: string[];
  onRemove: (barcode: string) => void;
  removing: string | null;
  error: string | null;
}) {
  const { t } = useTranslation();
  return (
    <View style={styles.field}>
      <Text role="heading" style={styles.label}>
        {t("editProduct.barcodesHeading")}
      </Text>
      {barcodes.length === 0 ? (
        <Text style={styles.text}>{t("editProduct.noBarcodes")}</Text>
      ) : (
        barcodes.map((barcode) => (
          <View key={barcode} style={styles.barcode}>
            <Text style={[styles.text, styles.barcodeText]}>{barcode}</Text>
            {removing === barcode ? (
              <ActivityIndicator
                accessibilityLabel={t("editProduct.removingBarcode", {
                  barcode,
                })}
              />
            ) : (
              <Pressable
                accessibilityLabel={t("editProduct.removeBarcodeLabel", {
                  barcode,
                })}
                disabled={removing !== null}
                hitSlop={8}
                onPress={() => onRemove(barcode)}
                role="button"
                style={({ pressed }) => [
                  styles.remove,
                  pressed && styles.pressed,
                ]}
              >
                <Text style={styles.removeText}>
                  {t("editProduct.removeBarcode")}
                </Text>
              </Pressable>
            )}
          </View>
        ))
      )}
      {error && (
        <Text role="alert" style={styles.error}>
          {error}
        </Text>
      )}
      {barcodes.length > 0 && (
        <Text style={styles.hint}>{t("editProduct.barcodesHint")}</Text>
      )}
    </View>
  );
}

/** The API's field names in `validation_failed` details, by form field. */
const ServerFieldNames: Record<string, ProductEditField> = {
  name: "name",
  description: "description",
  min_stock: "minStock",
};

/** The error for a failed save or barcode removal, when no field shows it. */
function errorText(
  error: Error,
  action: "saveFailed" | "removeBarcodeFailed",
  t: TFunction,
): string {
  if (!(error instanceof ApiResponseError)) {
    return t("editProduct.errors.unreachable");
  }
  if (error.status === 404) {
    return action === "saveFailed"
      ? t("editProduct.errors.notFound")
      : t("editProduct.errors.barcodeGone");
  }
  return t(`editProduct.errors.${action}`, { status: String(error.status) });
}

const styles = StyleSheet.create({
  container: {
    gap: 16,
    padding: 24,
  },
  field: {
    gap: 6,
  },
  label: {
    fontSize: 14,
    fontWeight: "600",
  },
  text: {
    fontSize: 16,
  },
  hint: {
    fontSize: 13,
  },
  barcode: {
    minHeight: 44,
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
  },
  barcodeText: {
    flex: 1,
  },
  remove: {
    minHeight: 44,
    justifyContent: "center",
    paddingHorizontal: 12,
    borderRadius: 8,
    borderWidth: 2,
    borderColor: "#b3261e",
  },
  pressed: {
    opacity: 0.7,
  },
  removeText: {
    color: "#b3261e",
    fontSize: 16,
    fontWeight: "600",
  },
  error: {
    color: "#b3261e",
    fontSize: 14,
  },
});
