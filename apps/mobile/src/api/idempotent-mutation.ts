import {
  hashKey,
  useMutation,
  type DefaultError,
  type MutateOptions,
  type UseMutationOptions,
  type UseMutationResult,
} from "@tanstack/react-query";
import { randomUUID } from "expo-crypto";
import { useCallback, useRef } from "react";

/** A request of a user action: its variables, and the `Idempotency-Key` it is sent with. */
interface Attempt<TVariables> {
  variables: TVariables;
  idempotencyKey: string;
}

/** The variables and key of the last attempt that failed, which sending the same variables retries. */
interface Failure {
  hash: string;
  idempotencyKey: string;
}

export interface IdempotentMutationOptions<
  TData,
  TError,
  TVariables,
  TOnMutateResult,
> extends Omit<
  UseMutationOptions<TData, TError, TVariables, TOnMutateResult>,
  "mutationFn"
> {
  /** Sends `variables` with `idempotencyKey` as the request's `Idempotency-Key` header. */
  mutationFn: (variables: TVariables, idempotencyKey: string) => Promise<TData>;
}

/**
 * A mutation for user actions that the server applies once however often their request is sent, such as
 * recording a stock movement (spec 3.2, rule 8). `mutationFn` gets the `Idempotency-Key` to send.
 *
 * Each `mutate` is a new action with a new key, so tapping "+1" twice adds two units, except when it sends
 * the same variables as the last attempt that failed: that retries the attempt with its key. If the
 * failed request did reach the server and only its answer was lost, the server then returns what it
 * recorded instead of recording it again. TanStack Query's own retries (`retry`) send the same key too.
 * After a success, or with changed variables, the next `mutate` is a new action again.
 */
export function useIdempotentMutation<
  TData = unknown,
  TError = DefaultError,
  TVariables = void,
  TOnMutateResult = unknown,
>({
  mutationFn,
  onMutate,
  onSuccess,
  onError,
  onSettled,
  ...options
}: IdempotentMutationOptions<
  TData,
  TError,
  TVariables,
  TOnMutateResult
>): UseMutationResult<TData, TError, TVariables, TOnMutateResult> {
  const lastFailure = useRef<Failure | null>(null);

  const mutation = useMutation<
    TData,
    TError,
    Attempt<TVariables>,
    TOnMutateResult
  >({
    ...options,
    mutationFn: async ({ variables, idempotencyKey }) => {
      try {
        const data = await mutationFn(variables, idempotencyKey);
        if (lastFailure.current?.idempotencyKey === idempotencyKey) {
          lastFailure.current = null;
        }
        return data;
      } catch (error) {
        lastFailure.current = { hash: hashOf(variables), idempotencyKey };
        throw error;
      }
    },
    ...(onMutate && {
      onMutate: (attempt, context) => onMutate(attempt.variables, context),
    }),
    // As `forAttempts` does, but these may return a promise, which TanStack Query awaits.
    ...(onSuccess && {
      onSuccess: (data, attempt, onMutateResult, context) =>
        onSuccess(data, attempt.variables, onMutateResult, context),
    }),
    ...(onError && {
      onError: (error, attempt, onMutateResult, context) =>
        onError(error, attempt.variables, onMutateResult, context),
    }),
    ...(onSettled && {
      onSettled: (data, error, attempt, onMutateResult, context) =>
        onSettled(data, error, attempt.variables, onMutateResult, context),
    }),
  });

  const attempt = useCallback((variables: TVariables): Attempt<TVariables> => {
    const failure = lastFailure.current;
    const idempotencyKey =
      failure?.hash === hashOf(variables)
        ? failure.idempotencyKey
        : randomUUID();
    return { variables, idempotencyKey };
  }, []);

  const { mutate: mutateAttempt, mutateAsync: mutateAttemptAsync } = mutation;
  const mutate = useCallback(
    (
      variables: TVariables,
      callbacks?: MutateOptions<TData, TError, TVariables, TOnMutateResult>,
    ) => mutateAttempt(attempt(variables), callbacks && forAttempts(callbacks)),
    [mutateAttempt, attempt],
  );
  const mutateAsync = useCallback(
    (
      variables: TVariables,
      callbacks?: MutateOptions<TData, TError, TVariables, TOnMutateResult>,
    ) =>
      mutateAttemptAsync(
        attempt(variables),
        callbacks && forAttempts(callbacks),
      ),
    [mutateAttemptAsync, attempt],
  );

  return {
    ...mutation,
    variables: mutation.variables?.variables,
    mutate,
    mutateAsync,
  } as UseMutationResult<TData, TError, TVariables, TOnMutateResult>;
}

/** Variables that hash the same are the same request, whatever the order of their properties. */
const hashOf = (variables: unknown) => hashKey([variables]);

/** The callbacks for `variables`, as callbacks for the attempts that send them. */
function forAttempts<TData, TError, TVariables, TOnMutateResult>({
  onSuccess,
  onError,
  onSettled,
}: MutateOptions<TData, TError, TVariables, TOnMutateResult>): MutateOptions<
  TData,
  TError,
  Attempt<TVariables>,
  TOnMutateResult
> {
  return {
    ...(onSuccess && {
      onSuccess: (data, attempt, onMutateResult, context) =>
        onSuccess(data, attempt.variables, onMutateResult, context),
    }),
    ...(onError && {
      onError: (error, attempt, onMutateResult, context) =>
        onError(error, attempt.variables, onMutateResult, context),
    }),
    ...(onSettled && {
      onSettled: (data, error, attempt, onMutateResult, context) =>
        onSettled(data, error, attempt.variables, onMutateResult, context),
    }),
  };
}
