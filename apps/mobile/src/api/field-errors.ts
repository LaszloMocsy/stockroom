import { ApiResponseError } from "@stockroom/api-client";

/**
 * The server's messages for the fields it refused, from `details.fields` of a `validation_failed` error,
 * by form field: `names` maps the API's field names to the form's. They are the server's English text:
 * forms check the common cases themselves, in the user's language.
 */
export function serverFieldErrors<Field extends string>(
  error: unknown,
  names: Record<string, Field>,
): Partial<Record<Field, string>> {
  if (
    !(error instanceof ApiResponseError) ||
    error.code !== "validation_failed"
  ) {
    return {};
  }
  const { details } = error;
  const fields =
    typeof details === "object" && details !== null && "fields" in details
      ? details.fields
      : null;
  if (typeof fields !== "object" || fields === null) {
    return {};
  }

  const errors: Partial<Record<Field, string>> = {};
  for (const [name, messages] of Object.entries(fields)) {
    const field = names[name];
    if (field && Array.isArray(messages) && typeof messages[0] === "string") {
      errors[field] = messages[0];
    }
  }
  return errors;
}
