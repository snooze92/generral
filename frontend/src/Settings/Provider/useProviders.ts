// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/useProviderSettings.ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import ModelBase from 'App/ModelBase';
import useApiMutation, {
  addOrUpdateQueryClientItem,
} from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { usePendingChangesStore } from 'Helpers/Hooks/usePendingChangesStore';
import { usePendingFieldsStore } from 'Helpers/Hooks/usePendingFieldsStore';
import { PendingSection } from 'typings/pending';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import selectSettings from 'Utilities/selectSettings';
import { ProviderBase } from './Provider';

const NONE: never[] = [];

const headers = {
  'X-Api-Key': window.Generral.apiKey,
  'X-Generral-Client': 'Generral',
};

export const useProviders = <T extends ProviderBase>(path: string) => {
  const result = useApiQuery<T[]>({ path });

  return { ...result, data: (result.data ?? NONE) as T[] };
};

export const useProviderSchema = <T extends ProviderBase>(
  path: string,
  enabled = true
) => {
  const result = useApiQuery<T[]>({
    path: `${path}/schema`,
    queryOptions: { enabled },
  });

  return { ...result, data: (result.data ?? NONE) as T[] };
};

export const useDeleteProvider = <T extends ModelBase>(
  path: string,
  id: number
) => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error, reset } = useApiMutation<void, void>({
    path: `${path}/${id}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.setQueryData<T[]>([path], (oldData = []) =>
          oldData.filter((item) => item.id !== id)
        );
      },
    },
  });

  return {
    deleteProvider: mutate,
    isDeleting: isPending,
    deleteError: error,
    resetDeleteError: reset,
  };
};

/** Only the fields' name and value are sent back, like Sonarr's providers. */
type ProviderRequest<T extends ProviderBase> = Omit<T, 'fields'> & {
  fields: { name: string; value: unknown }[];
};

function toRequest<T extends ProviderBase>(
  provider: T,
  changedFields: Map<string, unknown>
): ProviderRequest<T> {
  return {
    ...provider,
    fields: provider.fields.map((field) => ({
      name: field.name,
      value: changedFields.has(field.name)
        ? changedFields.get(field.name)
        : field.value,
    })),
  };
}

interface ManageProviderOptions<T extends ProviderBase> {
  path: string;
  id?: number;
  /** The schema entry to start a new provider from. */
  selectSchema: (schema: T[]) => T | undefined;
  onSaved: (provider: T) => void;
}

/** The pending state of a new (from a schema entry) or existing provider. */
export const useManageProvider = <T extends ProviderBase, TTestResult = unknown>({
  path,
  id,
  selectSchema,
  onSaved,
}: ManageProviderOptions<T>) => {
  const queryClient = useQueryClient();
  const { data: providers } = useProviders<T>(path);
  const { data: schema } = useProviderSchema<T>(path, !id);

  const provider = useMemo(() => {
    if (id) {
      return providers.find((p) => p.id === id);
    }

    const selected = selectSchema(schema);

    return selected ? { ...selected, id: 0 } : undefined;
  }, [id, providers, schema, selectSchema]);

  const { pendingChanges, setPendingChange } = usePendingChangesStore<T>({});
  const { pendingFields, setPendingFields } = usePendingFieldsStore();

  const save = useMutation<T, ApiError, ProviderRequest<T>>({
    mutationFn: (data) =>
      fetchJson<T, ProviderRequest<T>>({
        path: getQueryPath(data.id ? `${path}/${data.id}` : path),
        method: data.id ? 'PUT' : 'POST',
        headers,
        body: data,
      }),
    onSuccess: (saved) => {
      queryClient.setQueryData<T[]>([path], (oldData = []) =>
        addOrUpdateQueryClientItem(oldData, saved, 'id')
      );
      onSaved(saved);
    },
  });

  const test = useMutation<TTestResult, ApiError, ProviderRequest<T>>({
    mutationFn: (data) =>
      fetchJson<TTestResult, ProviderRequest<T>>({
        path: getQueryPath(`${path}/test`),
        method: 'POST',
        headers,
        body: data,
      }),
  });

  const mutationError =
    save.submittedAt >= test.submittedAt ? save.error : test.error;

  const changedValues = useMemo(() => {
    const changed: Partial<T> = {};

    (Object.keys(pendingChanges) as (keyof T)[]).forEach((key) => {
      if (provider?.[key] !== pendingChanges[key]) {
        changed[key] = pendingChanges[key];
      }
    });

    return changed;
  }, [provider, pendingChanges]);

  const changedFields = useMemo(() => {
    const changed = new Map<string, unknown>();

    pendingFields.forEach((value, name) => {
      if (provider?.fields.find((f) => f.name === name)?.value !== value) {
        changed.set(name, value);
      }
    });

    return changed;
  }, [provider, pendingFields]);

  const { settings: item, ...settings } = useMemo(() => {
    const combinedPendingChanges = changedFields.size
      ? { ...changedValues, fields: Object.fromEntries(changedFields) }
      : changedValues;

    return selectSettings(
      provider ?? {},
      combinedPendingChanges,
      mutationError
    );
  }, [provider, changedValues, changedFields, mutationError]);

  const current = useCallback(() => {
    if (!provider) {
      throw new Error(`Provider ${id ?? 'schema'} not found`);
    }

    return toRequest({ ...provider, ...changedValues }, changedFields);
  }, [id, provider, changedValues, changedFields]);

  const { mutate: saveMutate } = save;
  const { mutate: testMutate } = test;

  const saveProvider = useCallback(
    () => saveMutate(current()),
    [current, saveMutate]
  );
  const testProvider = useCallback(
    () => testMutate(current()),
    [current, testMutate]
  );

  return {
    ...settings,
    provider,
    item: item as PendingSection<T>,
    updateValue: setPendingChange,
    updateFieldValue: setPendingFields,
    saveProvider,
    isSaving: save.isPending,
    saveError: mutationError,
    testProvider,
    isTesting: test.isPending,
    testError: test.error,
    testResult: test.data,
  };
};
