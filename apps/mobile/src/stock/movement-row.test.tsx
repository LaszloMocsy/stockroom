import { describe, expect, it } from "@jest/globals";
import type { Schema } from "@stockroom/api-client";
import { act, render, screen } from "@testing-library/react-native";

import i18n from "@/i18n";

import { MovementRow } from "./movement-row";

const removal: Schema<"StockMovementResponse"> = {
  id: "0199d0a4-7b1e-7c3a-9f2d-4e5a6b7c8d90",
  product_id: "0b8e2c1a-5d7f-4e3b-9a61-2f4c8d9e1a37",
  product_sku: "WRK-CBT-200",
  product_name: "Cable ties 200 mm, bag of 100",
  type: "issue",
  delta: -1500,
  quantity_after: 2500,
  reason: "sale",
  note: "Order 1042",
  reference: null,
  voids_movement_id: null,
  actor_id: "6f1c1a52-8a1e-4c63-9a54-1e1f4b8f2d10",
  actor_name: "Sam Staff",
  created_at: "2026-10-09T14:05:00Z",
};

describe("MovementRow", () => {
  it("shows the type, change, reason, who, when, and the quantity after", async () => {
    await render(<MovementRow movement={removal} voided={false} />);

    expect(screen.getByText("Removed")).toBeOnTheScreen();
    expect(screen.getByText("−1,500")).toBeOnTheScreen();
    expect(screen.getByText("Sale · Sam Staff")).toBeOnTheScreen();
    // In the device's time zone and the app's language.
    const when = new Intl.DateTimeFormat("en", {
      dateStyle: "medium",
      timeStyle: "short",
    }).format(new Date(removal.created_at));
    expect(screen.getByText(`${when} · 2,500 on hand after`)).toBeOnTheScreen();
    expect(screen.getByText("Order 1042")).toBeOnTheScreen();
  });

  it("shows an increase with a plus sign", async () => {
    await render(
      <MovementRow
        movement={{ ...removal, type: "receive", delta: 3, reason: "purchase" }}
        voided={false}
      />,
    );

    expect(screen.getByText("Added")).toBeOnTheScreen();
    expect(screen.getByText("+3")).toBeOnTheScreen();
  });

  it("leaves out a missing note", async () => {
    await render(
      <MovementRow movement={{ ...removal, note: null }} voided={false} />,
    );

    expect(screen.queryByText("Order 1042")).toBeNull();
  });

  it("says when the movement was voided", async () => {
    await render(<MovementRow movement={removal} voided />);

    expect(screen.getByText("Removed (voided)")).toBeOnTheScreen();
  });

  it("takes its text from translation keys", async () => {
    // In i18next's "cimode", text is shown as its key.
    await i18n.changeLanguage("cimode");
    try {
      await render(<MovementRow movement={removal} voided={false} />);

      expect(screen.getByText("movement.types.issue")).toBeOnTheScreen();
      expect(screen.getByText("movement.reasonAndActor")).toBeOnTheScreen();
    } finally {
      // Re-renders the component, so inside act.
      await act(() => i18n.changeLanguage("en"));
    }
  });
});
