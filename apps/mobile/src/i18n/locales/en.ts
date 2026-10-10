/**
 * The app's English text, the source for every other language. Keys group text by where it appears;
 * `{{name}}` marks a value filled in at run time.
 */
export const en = {
  app: {
    name: "Stockroom",
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
