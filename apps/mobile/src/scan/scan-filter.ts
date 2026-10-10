/** How long a barcode has to be out of view before seeing it again counts as a new scan. */
export const RescanAfterMs = 1500;

/**
 * Turns the camera's stream of reads into one scan per detection. The camera reports a barcode on every
 * frame it is in view; the filter passes the first read and drops the rest until the barcode has not
 * been seen for `rescanAfterMs`. Each barcode is tracked on its own, so two in view at once scan once
 * each.
 *
 * Returns whether the read of `barcode` at `now` (milliseconds) is a new scan.
 */
export function createScanFilter(rescanAfterMs = RescanAfterMs) {
  const lastSeen = new Map<string, number>();

  return (barcode: string, now: number): boolean => {
    for (const [seen, at] of lastSeen) {
      if (now - at >= rescanAfterMs) {
        lastSeen.delete(seen);
      }
    }
    const isNew = barcode !== "" && !lastSeen.has(barcode);
    lastSeen.set(barcode, now);
    return isNew;
  };
}
