import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import { act, render, screen } from "@testing-library/react-native";
import {
  addNetworkStateListener,
  getNetworkStateAsync,
  NetworkStateType,
  type NetworkState,
} from "expo-network";
import { AccessibilityInfo } from "react-native";

import { OfflineBanner } from "./offline-banner";
import { isOffline } from "./use-offline";

jest.mock("expo-network", () => ({
  ...jest.requireActual<object>("expo-network"),
  addNetworkStateListener: jest.fn(),
  getNetworkStateAsync: jest.fn(),
}));

const online: NetworkState = {
  type: NetworkStateType.WIFI,
  isConnected: true,
  isInternetReachable: true,
};
const offline: NetworkState = {
  type: NetworkStateType.NONE,
  isConnected: false,
  isInternetReachable: false,
};

let listeners: ((state: NetworkState) => void)[];
let resolveReading: (state: NetworkState) => void;
let announce: jest.SpiedFunction<
  typeof AccessibilityInfo.announceForAccessibility
>;

beforeEach(() => {
  listeners = [];
  jest.mocked(addNetworkStateListener).mockImplementation((listener) => {
    listeners.push(listener);
    return {
      remove: () => {
        listeners = listeners.filter((other) => other !== listener);
      },
    };
  });
  jest.mocked(getNetworkStateAsync).mockReturnValue(
    new Promise((resolve) => {
      resolveReading = resolve;
    }),
  );
  announce = jest
    .spyOn(AccessibilityInfo, "announceForAccessibility")
    .mockImplementation(() => {});
});

afterEach(() => {
  announce.mockRestore();
});

/** Reports a change of the network, as the device does when it drops or returns. */
async function networkChanges(state: NetworkState) {
  await act(async () => {
    for (const listener of listeners) {
      listener(state);
    }
  });
}

async function firstReading(state: NetworkState) {
  await act(async () => resolveReading(state));
}

const banner = () => screen.queryByTestId("offline-banner");
const message =
  "You're offline. Changes can't be saved until you're back online.";

describe("OfflineBanner", () => {
  it("shows nothing while online", async () => {
    await render(<OfflineBanner />);
    await firstReading(online);

    expect(banner()).toBeNull();
    expect(announce).not.toHaveBeenCalled();
  });

  it("shows nothing until the first reading arrives", async () => {
    await render(<OfflineBanner />);

    expect(banner()).toBeNull();
  });

  it("shows when the app starts offline", async () => {
    await render(<OfflineBanner />);
    await firstReading(offline);

    expect(screen.getByText(message)).toBeOnTheScreen();
  });

  it("appears as the network drops and clears as it returns", async () => {
    await render(<OfflineBanner />);
    await firstReading(online);

    await networkChanges(offline);
    expect(screen.getByText(message)).toBeOnTheScreen();
    expect(announce).toHaveBeenLastCalledWith(message);

    await networkChanges(online);
    expect(banner()).toBeNull();
    expect(announce).toHaveBeenLastCalledWith("You're back online.");
    expect(announce).toHaveBeenCalledTimes(2);
  });

  it("keeps a change that arrives before the first reading", async () => {
    await render(<OfflineBanner />);
    await networkChanges(offline);
    await firstReading(online);

    expect(banner()).toBeOnTheScreen();
  });

  it("counts an unreadable network state as online", async () => {
    jest
      .mocked(getNetworkStateAsync)
      .mockRejectedValue(new Error("unavailable"));
    await render(<OfflineBanner />);
    await act(async () => {});

    expect(banner()).toBeNull();
  });

  it("stops listening when it unmounts", async () => {
    const { unmount } = await render(<OfflineBanner />);
    await unmount();

    expect(listeners).toHaveLength(0);
  });
});

describe("isOffline", () => {
  it("is true without a network connection", () => {
    expect(isOffline(offline)).toBe(true);
  });

  it("is false on a network without internet, which may still reach a local server", () => {
    expect(
      isOffline({
        type: NetworkStateType.WIFI,
        isConnected: true,
        isInternetReachable: false,
      }),
    ).toBe(false);
  });

  it("is false while the state is unknown", () => {
    expect(isOffline({})).toBe(false);
  });
});
