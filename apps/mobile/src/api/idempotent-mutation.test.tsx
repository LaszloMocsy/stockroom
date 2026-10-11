import { afterEach, describe, expect, it, jest } from "@jest/globals";
import { notifyManager, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react-native";
import type { ReactNode } from "react";

import { useIdempotentMutation } from "./idempotent-mutation";
import { createQueryClient } from "./query-client";

// Numbered keys, so a test can tell which action a key was made for.
let mockKeys = 0;
jest.mock("expo-crypto", () => ({ randomUUID: () => `key-${++mockKeys}` }));

interface Receive {
  product: string;
  quantity: number;
}

// Tells the hook about a mutation's progress in a microtask rather than a timer, so within the `act` that
// sent it.
notifyManager.setScheduler(queueMicrotask);

const queryClient = createQueryClient();
// Keeps finished mutations rather than starting timers to drop them, which would keep the test run alive.
queryClient.setDefaultOptions({
  ...queryClient.getDefaultOptions(),
  mutations: { gcTime: Infinity },
});

function wrapper({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
}

afterEach(() => {
  queryClient.clear();
  mockKeys = 0;
});

/** A request that the connection drops, so the client cannot tell whether the server recorded it. */
const dropped = () => Promise.reject(new TypeError("Network request failed"));

/** A mutation whose `send` records the keys it gets, and answers with the key. */
async function renderMutation(
  options: {
    retry?: number;
    onSuccess?: (data: string, variables: Receive) => void;
  } = {},
) {
  const send = jest.fn((_: Receive, key: string) => Promise.resolve(key));
  const rendered = await renderHook(
    () =>
      useIdempotentMutation({
        mutationFn: send,
        retryDelay: 0,
        ...options,
      }),
    { wrapper },
  );
  /** Sends `variables`, and settles whether or not the request fails. */
  const mutate = (variables: Receive) =>
    act(() => rendered.result.current.mutateAsync(variables).catch(() => {}));
  const sentKeys = () => send.mock.calls.map(([, key]) => key);
  return { ...rendered, send, mutate, sentKeys };
}

const twoBolts: Receive = { product: "bolt", quantity: 2 };

describe("useIdempotentMutation", () => {
  it("sends each action with a new key", async () => {
    const { mutate, sentKeys } = await renderMutation();

    await mutate(twoBolts);
    await mutate(twoBolts);

    expect(sentKeys()).toEqual(["key-1", "key-2"]);
  });

  it("retries a failed action with its key", async () => {
    const { send, mutate, sentKeys, result } = await renderMutation();
    send.mockImplementationOnce(dropped);

    await mutate(twoBolts);
    // The same request, built again from the form's values.
    await mutate({ quantity: 2, product: "bolt" });

    expect(sentKeys()).toEqual(["key-1", "key-1"]);
    expect(result.current.data).toBe("key-1");
  });

  it("starts a new action after the retry succeeds", async () => {
    const { send, mutate, sentKeys } = await renderMutation();
    send.mockImplementationOnce(dropped);

    await mutate(twoBolts);
    await mutate(twoBolts);
    await mutate(twoBolts);

    expect(sentKeys()).toEqual(["key-1", "key-1", "key-2"]);
  });

  it("keeps the key while the retries fail", async () => {
    const { send, mutate, sentKeys } = await renderMutation();
    send.mockImplementationOnce(dropped).mockImplementationOnce(dropped);

    await mutate(twoBolts);
    await mutate(twoBolts);
    await mutate(twoBolts);

    expect(sentKeys()).toEqual(["key-1", "key-1", "key-1"]);
  });

  it("sends changed variables after a failure as a new action", async () => {
    const { send, mutate, sentKeys } = await renderMutation();
    send.mockImplementationOnce(dropped);

    await mutate(twoBolts);
    await mutate({ product: "bolt", quantity: 3 });

    expect(sentKeys()).toEqual(["key-1", "key-2"]);
  });

  it("sends TanStack Query's retries with the same key", async () => {
    const { send, mutate, sentKeys } = await renderMutation({ retry: 1 });
    send.mockImplementationOnce(dropped);

    await mutate(twoBolts);

    expect(sentKeys()).toEqual(["key-1", "key-1"]);
  });

  it("gives actions sent at the same time their own keys", async () => {
    const { send, result, sentKeys } = await renderMutation();
    const answers: ((key: string) => void)[] = [];
    send.mockImplementation(
      (_, key) => new Promise((resolve) => answers.push(() => resolve(key))),
    );

    await act(() => {
      result.current.mutate(twoBolts);
      result.current.mutate(twoBolts);
    });
    await waitFor(() => expect(send).toHaveBeenCalledTimes(2));

    expect(sentKeys()).toEqual(["key-1", "key-2"]);
    await act(() => answers.forEach((answer) => answer("")));
  });

  it("gives the callbacks and the result the variables, not the key", async () => {
    const onSuccess = jest.fn();
    const onMutateSuccess = jest.fn();
    const { result } = await renderMutation({ onSuccess });

    await act(() =>
      result.current.mutate(twoBolts, { onSuccess: onMutateSuccess }),
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.variables).toEqual(twoBolts);
    expect(onSuccess).toHaveBeenCalledWith(
      "key-1",
      twoBolts,
      undefined,
      expect.anything(),
    );
    expect(onMutateSuccess).toHaveBeenCalledWith(
      "key-1",
      twoBolts,
      undefined,
      expect.anything(),
    );
  });
});
