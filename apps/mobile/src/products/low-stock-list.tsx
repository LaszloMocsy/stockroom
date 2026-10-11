import { useTranslation } from "react-i18next";

import { ProductPages } from "./product-pages";
import { useProducts } from "./use-products";

/**
 * Every active product at or below its `min_stock`, out of stock ones included, sorted by name (spec
 * 4.3). Each one says in words, with an icon, whether it is low or out.
 */
export function LowStockList() {
  const { t } = useTranslation();
  const products = useProducts({ lowStock: true });
  return (
    <ProductPages
      emptyMessage={t("lowStock.none")}
      products={products}
      testID="low-stock-list"
    />
  );
}
