import { ApiResponseError, unwrap, type Schema } from "@stockroom/api-client";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { TFunction } from "i18next";
import { useTranslation } from "react-i18next";
import { Alert, StyleSheet, Text, View } from "react-native";

import { useApiClient } from "@/api/provider";
import { Button } from "@/components/button";
import { useToast } from "@/components/toast";
import { useIsAdmin } from "@/session/use-me";

import { productKey } from "./use-products";

type Product = Schema<"ProductResponse">;
type Action = "archive" | "restore";

/**
 * Archives the product, or restores an archived one (spec 4.1). Only ADMIN users see it (spec 2.1);
 * the server refuses anyone else too. Archiving asks first, as it hides the product from the lists and
 * search; its stock and history stay. Restoring needs no confirmation, as it only undoes that.
 */
export function ArchiveActions({ product }: { product: Product }) {
  const { t } = useTranslation();
  const client = useApiClient();
  const queryClient = useQueryClient();
  const { show } = useToast();
  const isAdmin = useIsAdmin();

  const change = useMutation({
    mutationFn: (action: Action) =>
      unwrap(
        action === "archive"
          ? client.POST("/api/v1/products/{id}/archive", {
              params: { path: { id: product.id } },
            })
          : client.POST("/api/v1/products/{id}/restore", {
              params: { path: { id: product.id } },
            }),
      ),
    onSuccess: (updated, action) => {
      queryClient.setQueryData(productKey(updated.id), updated);
      // The lists and Home's figures leave out archived products.
      void Promise.all([
        queryClient.invalidateQueries({ queryKey: ["products"] }),
        queryClient.invalidateQueries({ queryKey: ["stats"] }),
      ]);
      show({ message: t(`archive.${action}.done`, { name: updated.name }) });
    },
    onError: (error, action) => {
      // The user is no longer an ADMIN: loading their role again hides these buttons.
      if (error instanceof ApiResponseError && error.status === 403) {
        void queryClient.invalidateQueries({ queryKey: ["me"] });
      }
      show({ message: errorText(error, action, t) });
    },
  });

  if (!isAdmin) {
    return null;
  }

  const archived = product.archived_at !== null;
  const confirmArchive = () =>
    Alert.alert(
      t("archive.confirmTitle"),
      t("archive.confirmMessage", { name: product.name }),
      [
        { text: t("common.cancel"), style: "cancel" },
        {
          text: t("archive.archive.button"),
          style: "destructive",
          onPress: () => change.mutate("archive"),
        },
      ],
    );

  return (
    <View style={styles.section}>
      <Text style={styles.hint}>
        {t(`archive.${archived ? "restore" : "archive"}.hint`)}
      </Text>
      <Button
        busy={change.isPending}
        onPress={archived ? () => change.mutate("restore") : confirmArchive}
        title={
          change.isPending
            ? t(`archive.${change.variables}.pending`)
            : t(`archive.${archived ? "restore" : "archive"}.button`)
        }
        variant="secondary"
      />
    </View>
  );
}

function errorText(error: Error, action: Action, t: TFunction): string {
  if (!(error instanceof ApiResponseError)) {
    return t("archive.errors.unreachable");
  }
  if (error.status === 403) {
    return t("archive.errors.forbidden");
  }
  if (error.status === 404) {
    return t("archive.errors.notFound");
  }
  return t(`archive.${action}.failed`, { status: String(error.status) });
}

const styles = StyleSheet.create({
  section: {
    gap: 8,
  },
  hint: {
    fontSize: 14,
  },
});
