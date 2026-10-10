import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import { act, fireEvent, render, screen } from "@testing-library/react-native";
import {
  PermissionStatus,
  useCameraPermissions,
  type BarcodeScanningResult,
  type PermissionResponse,
} from "expo-camera";
import { AppState, Linking, type AppStateStatus } from "react-native";

import { BarcodeScanner } from "./barcode-scanner";
import { RescanAfterMs } from "./scan-filter";

jest.mock("expo-camera", () => {
  const { View } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    ...jest.requireActual<object>("expo-camera"),
    // A plain view, so tests can read the props and fire `onBarcodeScanned`.
    CameraView: View,
    useCameraPermissions: jest.fn(),
  };
});

const useCameraPermissionsMock = jest.mocked(useCameraPermissions);
const requestPermission = jest.fn<() => Promise<PermissionResponse>>();
const getPermission = jest.fn<() => Promise<PermissionResponse>>();

function permission(
  status: PermissionStatus,
  canAskAgain = true,
): PermissionResponse {
  return {
    status,
    granted: status === PermissionStatus.GRANTED,
    canAskAgain,
    expires: "never",
  };
}

/** Makes the camera permission hook report this permission, `null` while it is being read. */
function givenPermission(response: PermissionResponse | null) {
  useCameraPermissionsMock.mockReturnValue([
    response,
    requestPermission,
    getPermission,
  ]);
}

const camera = () => screen.getByTestId("barcode-scanner-camera");

/** Has the camera read this barcode, as it does on every frame the barcode is in view. */
async function read(data: string) {
  const result: BarcodeScanningResult = {
    type: "ean13",
    data,
    cornerPoints: [],
    bounds: { origin: { x: 0, y: 0 }, size: { width: 0, height: 0 } },
  };
  await act(() => camera().props.onBarcodeScanned(result));
}

beforeEach(() => {
  jest.useFakeTimers({ now: new Date("2026-10-10T08:00:00Z") });
});

afterEach(() => {
  jest.useRealTimers();
  jest.clearAllMocks();
});

describe("BarcodeScanner", () => {
  it("waits while the permission is being read", async () => {
    givenPermission(null);

    await render(<BarcodeScanner onScan={jest.fn()} />);

    expect(screen.queryByTestId("barcode-scanner-camera")).toBeNull();
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("asks for the camera when it has not been allowed yet", async () => {
    givenPermission(permission(PermissionStatus.UNDETERMINED));

    await render(<BarcodeScanner onScan={jest.fn()} />);

    expect(
      screen.getByText("Stockroom needs the camera to scan barcodes."),
    ).toBeOnTheScreen();
    expect(screen.queryByTestId("barcode-scanner-camera")).toBeNull();

    await fireEvent.press(screen.getByRole("button", { name: "Allow camera" }));

    expect(requestPermission).toHaveBeenCalledTimes(1);
  });

  it("asks again when the user declined but the system still lets the app ask", async () => {
    givenPermission(permission(PermissionStatus.DENIED, true));

    await render(<BarcodeScanner onScan={jest.fn()} />);

    expect(
      screen.getByRole("button", { name: "Allow camera" }),
    ).toBeOnTheScreen();
  });

  it("sends the user to Settings when the camera has been turned down for good", async () => {
    const openSettings = jest
      .spyOn(Linking, "openSettings")
      .mockResolvedValue(undefined);
    givenPermission(permission(PermissionStatus.DENIED, false));

    await render(<BarcodeScanner onScan={jest.fn()} />);

    expect(
      screen.getByText(
        "Stockroom cannot use the camera. To scan barcodes, allow camera access in Settings.",
      ),
    ).toBeOnTheScreen();

    await fireEvent.press(
      screen.getByRole("button", { name: "Open Settings" }),
    );

    expect(openSettings).toHaveBeenCalledTimes(1);
    expect(requestPermission).not.toHaveBeenCalled();
    openSettings.mockRestore();
  });

  it("checks the permission again when the app returns to the foreground", async () => {
    let onChange: ((status: AppStateStatus) => void) | undefined;
    const addEventListener = jest
      .spyOn(AppState, "addEventListener")
      .mockImplementation((_type, listener) => {
        onChange = listener;
        return { remove: jest.fn() };
      });
    givenPermission(permission(PermissionStatus.DENIED, false));

    await render(<BarcodeScanner onScan={jest.fn()} />);
    await act(() => onChange?.("active"));

    expect(getPermission).toHaveBeenCalledTimes(1);
    addEventListener.mockRestore();
  });

  it("shows the back camera, scanning the barcodes products carry, once allowed", async () => {
    givenPermission(permission(PermissionStatus.GRANTED));

    await render(<BarcodeScanner onScan={jest.fn()} />);

    expect(camera().props).toMatchObject({
      active: true,
      facing: "back",
      barcodeScannerSettings: {
        barcodeTypes: ["ean13", "ean8", "upc_a", "upc_e", "code128", "qr"],
      },
    });
    expect(
      screen.getByText("Point the camera at a barcode."),
    ).toBeOnTheScreen();
  });

  it("emits a barcode once per detection", async () => {
    givenPermission(permission(PermissionStatus.GRANTED));
    const onScan = jest.fn();

    await render(<BarcodeScanner onScan={onScan} />);

    // Held in view: read on frame after frame.
    await read("4006381333931");
    jest.advanceTimersByTime(100);
    await read("4006381333931");
    jest.advanceTimersByTime(100);
    await read("4006381333931");
    expect(onScan.mock.calls).toEqual([["4006381333931"]]);

    // Another barcode comes into view.
    await read("0012345678905");
    expect(onScan.mock.calls).toEqual([["4006381333931"], ["0012345678905"]]);

    // The first one is taken away, then shown again.
    jest.advanceTimersByTime(RescanAfterMs);
    await read("4006381333931");
    expect(onScan.mock.calls).toEqual([
      ["4006381333931"],
      ["0012345678905"],
      ["4006381333931"],
    ]);
  });

  it("turns the camera off and stops scanning while paused", async () => {
    givenPermission(permission(PermissionStatus.GRANTED));

    await render(<BarcodeScanner onScan={jest.fn()} paused />);

    expect(camera().props.active).toBe(false);
    expect(camera().props.onBarcodeScanned).toBeUndefined();
  });

  it("does not scan a barcode again that is still in view when it resumes", async () => {
    givenPermission(permission(PermissionStatus.GRANTED));
    const onScan = jest.fn();
    const { rerender } = await render(<BarcodeScanner onScan={onScan} />);
    await read("4006381333931");

    // For example, the scan opened a product, and the user went back to the scanner.
    await rerender(<BarcodeScanner onScan={onScan} paused />);
    jest.advanceTimersByTime(60_000);
    await rerender(<BarcodeScanner onScan={onScan} />);
    await read("4006381333931");
    expect(onScan).toHaveBeenCalledTimes(1);

    jest.advanceTimersByTime(RescanAfterMs);
    await read("4006381333931");
    expect(onScan).toHaveBeenCalledTimes(2);
  });
});
