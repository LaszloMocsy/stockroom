import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import { act, renderHook } from "@testing-library/react-native";

import { useDebouncedValue } from "./use-debounced-value";

beforeEach(() => {
  jest.useFakeTimers();
});

afterEach(() => {
  jest.useRealTimers();
});

describe("useDebouncedValue", () => {
  it("starts with the value", async () => {
    const { result } = await renderHook(() => useDebouncedValue("cab", 300));

    expect(result.current).toBe("cab");
  });

  it("takes a new value once it has not changed for the delay", async () => {
    const { result, rerender } = await renderHook(
      ({ value }: { value: string }) => useDebouncedValue(value, 300),
      { initialProps: { value: "" } },
    );

    await rerender({ value: "c" });
    await act(() => jest.advanceTimersByTime(200));
    await rerender({ value: "ca" });
    await act(() => jest.advanceTimersByTime(200));
    // 400 ms since the first change, but only 200 since the last.
    expect(result.current).toBe("");

    await act(() => jest.advanceTimersByTime(100));
    expect(result.current).toBe("ca");
  });
});
