import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { useTranslation } from "react-i18next";
import { Pressable, StyleSheet, Text } from "react-native";

import { ProductDetail } from "@/products/product-detail";

export default function ProductScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { t } = useTranslation();
  const router = useRouter();
  return (
    <>
      <Stack.Screen
        options={{
          headerRight: () => (
            <Pressable
              accessibilityLabel={t("product.editLabel")}
              hitSlop={8}
              onPress={() =>
                router.push({ pathname: "/product/edit", params: { id } })
              }
              role="button"
              style={styles.edit}
            >
              <Text style={styles.editText}>{t("product.edit")}</Text>
            </Pressable>
          ),
        }}
      />
      <ProductDetail id={id} />
    </>
  );
}

const styles = StyleSheet.create({
  edit: {
    minHeight: 44,
    justifyContent: "center",
    paddingHorizontal: 8,
  },
  editText: {
    color: "#0a5cc2",
    fontSize: 17,
  },
});
