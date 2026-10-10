/**
 * The app's English text, the source for every other language. Keys group text by where it appears;
 * `{{name}}` marks a value filled in at run time.
 */
export const en = {
  app: {
    name: "Stockroom",
  },
  connect: {
    title: "Connect to server",
    intro:
      "Enter the address of your Stockroom server. Your administrator can tell you what it is.",
    addressLabel: "Server address",
    addressPlaceholder: "stock.example.com",
    submit: "Connect",
    connecting: "Connecting…",
    errors: {
      malformed:
        "This is not a server address. Enter one like stock.example.com or https://stock.example.com.",
      insecure: "{{url}} is not encrypted. Use the server's https:// address.",
      unreachable:
        "Cannot reach {{url}}. Check the address and your network connection.",
      serverError:
        "The server at {{url}} answered with an error (HTTP {{status}}). Try again later.",
      notStockroom: "{{url}} is not a Stockroom server. Check the address.",
      appOutdated:
        "{{url}} needs a newer version of the app than {{clientVersion}}. Please update the app.",
      serverOutdated:
        "The server at {{url}} is outdated. Ask your administrator to update it.",
      saveFailed: "The server address could not be saved. Try again.",
    },
  },
  compatibility: {
    title: "Update needed",
    appOutdated: "Please update the app. This server needs a newer version.",
    serverOutdated:
      "This server is outdated. Ask your administrator to update it.",
    versions:
      "App {{clientVersion}}, server {{serverVersion}} (API {{apiVersion}})",
    retry: "Try again",
    checking: "Checking…",
  },
  home: {
    comingSoon: "Mobile app coming soon.",
  },
  serverStatus: {
    noServer: "No server is set.",
    connecting: "Connecting to {{serverUrl}}…",
    connected:
      "Connected to {{serverUrl}}: server {{serverVersion}}, API {{apiVersion}}",
    unreachable: "Cannot reach {{serverUrl}}: {{message}}",
  },
} as const;

type Strings<T> = {
  [Key in keyof T]: T[Key] extends string ? string : Strings<T[Key]>;
};

/** The shape every language follows: the same keys as English, so a missing key fails the typecheck. */
export type Translations = Strings<typeof en>;
