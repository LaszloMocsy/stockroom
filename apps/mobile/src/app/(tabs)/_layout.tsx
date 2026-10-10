import { Tabs } from "expo-router";
import { SymbolView, type SymbolViewProps } from "expo-symbols";
import { useTranslation } from "react-i18next";
import type { ColorValue } from "react-native";

/** A tab's icon: an SF Symbol on iOS, a Material Symbol on Android. Decorative, as the label names the tab. */
function tabIcon(name: SymbolViewProps["name"]) {
  function TabIcon({ color, size }: { color: ColorValue; size: number }) {
    return <SymbolView name={name} size={size} tintColor={color} />;
  }
  return TabIcon;
}

/** The signed-in app (spec 1.2): Home, Products, Scan, and Settings, one tap apart. */
export default function TabsLayout() {
  const { t } = useTranslation();

  return (
    <Tabs>
      <Tabs.Screen
        name="index"
        options={{
          title: t("tabs.home"),
          tabBarIcon: tabIcon({ ios: "house", android: "home" }),
        }}
      />
      <Tabs.Screen
        name="products"
        options={{
          title: t("tabs.products"),
          tabBarIcon: tabIcon({ ios: "shippingbox", android: "inventory_2" }),
        }}
      />
      <Tabs.Screen
        name="scan"
        options={{
          title: t("tabs.scan"),
          tabBarIcon: tabIcon({
            ios: "barcode.viewfinder",
            android: "barcode_scanner",
          }),
        }}
      />
      <Tabs.Screen
        name="settings"
        options={{
          title: t("tabs.settings"),
          tabBarIcon: tabIcon({ ios: "gearshape", android: "settings" }),
        }}
      />
    </Tabs>
  );
}
