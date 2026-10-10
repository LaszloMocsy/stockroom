/**
 * The app's English text, the source for every other language. Keys group text by where it appears;
 * `{{name}}` marks a value filled in at run time.
 */
export const en = {
  app: {
    name: "Stockroom",
  },
  common: {
    cancel: "Cancel",
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
  login: {
    title: "Sign in",
    intro: "Sign in to {{serverUrl}}.",
    usernameLabel: "Username",
    passwordLabel: "Password",
    submit: "Sign in",
    submitting: "Signing in…",
    changeServer: "Use a different server",
    errors: {
      required: "This is required.",
      invalidCredentials: "The username or password is wrong.",
      lockedOut_one: "Too many wrong passwords. Try again in {{count}} minute.",
      lockedOut_other:
        "Too many wrong passwords. Try again in {{count}} minutes.",
      rateLimited_one: "Too many attempts. Try again in {{count}} minute.",
      rateLimited_other: "Too many attempts. Try again in {{count}} minutes.",
      serverError:
        "The server could not sign you in (HTTP {{status}}). Try again.",
      unreachable:
        "Cannot reach the server. Check your network connection and try again.",
    },
  },
  setup: {
    title: "Set up Stockroom",
    intro:
      "This server has no accounts yet. Create the first administrator account; you can add other people later.",
    displayNameLabel: "Your name",
    displayNameHint: "Shown next to the changes you make.",
    usernameLabel: "Username",
    usernameHint: "Letters, digits, and - . _ @ +",
    passwordLabel: "Password",
    passwordHint: "At least {{min}} characters.",
    repeatedPasswordLabel: "Repeat password",
    submit: "Create account",
    submitting: "Creating account…",
    errors: {
      required: "This is required.",
      usernameCharacters: "Use only letters, digits, and - . _ @ +",
      passwordTooShort: "Use at least {{min}} characters.",
      passwordsDiffer: "The passwords do not match.",
      alreadyCompleted: "This server has already been set up.",
      rateLimited: "Too many attempts. Wait a minute and try again.",
      serverError:
        "The server could not create the account (HTTP {{status}}). Try again.",
      unreachable:
        "Cannot reach the server. Check your network connection and try again.",
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
  settings: {
    title: "Settings",
    accountHeading: "Account",
    signedInAs: "Signed in as {{name}} ({{username}})",
    signOut: "Sign out",
    signingOut: "Signing out…",
    serverHeading: "Server",
    changeServer: "Change server",
    changingServer: "Changing server…",
    changeServerTitle: "Change server?",
    changeServerMessage:
      "You will be signed out of {{serverUrl}}, and the app will ask for a server address again.",
  },
  home: {
    comingSoon: "Mobile app coming soon.",
    settings: "Settings",
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
