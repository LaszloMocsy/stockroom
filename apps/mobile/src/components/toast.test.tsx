import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import { act, fireEvent, render, screen } from "@testing-library/react-native";
import { useEffect } from "react";
import { AccessibilityInfo } from "react-native";

import { Toast, useToast, type ToastOptions } from "./toast";

let announce: jest.SpiedFunction<
  typeof AccessibilityInfo.announceForAccessibility
>;

beforeEach(() => {
  jest.useFakeTimers();
  announce = jest
    .spyOn(AccessibilityInfo, "announceForAccessibility")
    .mockImplementation(() => {});
});

afterEach(() => {
  jest.useRealTimers();
  announce.mockRestore();
});

/** Shows `options` once, as a screen would after an action. */
function Screen({ options }: { options: ToastOptions }) {
  const { toast, show, hide } = useToast();
  useEffect(() => show(options), [show, options]);
  return <Toast hide={hide} toast={toast} />;
}

describe("Toast", () => {
  it("shows a message, announces it, and hides it after four seconds", async () => {
    await render(<Screen options={{ message: "Saved." }} />);

    expect(screen.getByText("Saved.")).toBeOnTheScreen();
    expect(announce).toHaveBeenCalledWith("Saved.");

    await act(() => jest.advanceTimersByTime(3900));
    expect(screen.getByText("Saved.")).toBeOnTheScreen();
    await act(() => jest.advanceTimersByTime(100));
    expect(screen.queryByText("Saved.")).toBeNull();
  });

  it("gives an action eight seconds, and hides when it is pressed", async () => {
    const onPress = jest.fn();
    await render(
      <Screen
        options={{ message: "Added 1.", action: { label: "Undo", onPress } }}
      />,
    );

    await act(() => jest.advanceTimersByTime(7900));
    await fireEvent.press(screen.getByRole("button", { name: "Undo" }));

    expect(onPress).toHaveBeenCalledTimes(1);
    expect(screen.queryByText("Added 1.")).toBeNull();
  });

  it("keeps a persistent message", async () => {
    await render(
      <Screen options={{ message: "Undoing…", persistent: true }} />,
    );

    await act(() => jest.advanceTimersByTime(60_000));

    expect(screen.getByText("Undoing…")).toBeOnTheScreen();
  });
});
