import { useEffect, useState } from "react";

/**
 * `value` once it has stopped changing for `delayMs`, for example a search text once the user pauses
 * typing, so a request is not sent for every keystroke.
 */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timeout = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timeout);
  }, [value, delayMs]);

  return debounced;
}
