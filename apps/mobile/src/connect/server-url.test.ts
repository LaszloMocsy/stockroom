import { describe, expect, it } from "@jest/globals";

import { normaliseServerUrl } from "./server-url";

describe("normaliseServerUrl", () => {
  it.each([
    ["https://stock.example.com", "https://stock.example.com"],
    ["stock.example.com", "https://stock.example.com"],
    ["  Stock.Example.COM/  ", "https://stock.example.com"],
    ["HTTPS://stock.example.com", "https://stock.example.com"],
    ["http://192.168.1.20:5278", "http://192.168.1.20:5278"],
    ["localhost:5278", "https://localhost:5278"],
    ["https://stock.example.com:443/", "https://stock.example.com"],
    ["https://example.com/stockroom//", "https://example.com/stockroom"],
    ["https://stock.example.com/api/v1/", "https://stock.example.com"],
    ["https://example.com/stockroom/api/v1", "https://example.com/stockroom"],
    ["https://stock.example.com/?tab=1#top", "https://stock.example.com"],
  ])("normalises %j to %j", (input, expected) => {
    expect(normaliseServerUrl(input)).toBe(expected);
  });

  it.each([
    "",
    "   ",
    "https://",
    "stock example.com",
    "https:/stock.example.com",
    "ftp://stock.example.com",
    "mailto:admin@example.com",
    "https://admin:secret@stock.example.com",
    "https://[::1",
  ])("refuses %j", (input) => {
    expect(normaliseServerUrl(input)).toBeNull();
  });
});
