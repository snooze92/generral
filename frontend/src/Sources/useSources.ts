// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/useProviderSettings.ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
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
import Source, { SourceEvent, SourceTestResult } from './Source';

export const PATH = '/source';

const NO_SOURCES: Source[] = [];
const NO_EVENTS: SourceEvent[] = [];

const headers = {
  'X-Api-Key': window.Generral.apiKey,
  'X-Generral-Client': 'Generral',
};

export const useSources = () => {
  const result = useApiQuery<Source[]>({ path: PATH });

  return { ...result, data: result.data ?? NO_SOURCES };
};

export const useSource = (id: number | undefined) => {
  const { data, ...result } = useSources();

  return { ...result, data: data.find((s) => s.id === id) };
};

export const useSourceSchema = (enabled = true) => {
  const result = useApiQuery<Source[]>({
    path: `${PATH}/schema`,
    queryOptions: { enabled },
  });

  return { ...result, data: result.data ?? NO_SOURCES };
};

export const useSourceEvents = (id: number, includeGone: boolean) => {
  const result = useApiQuery<SourceEvent[]>({
    path: `${PATH}/${id}/events`,
    queryParams: { includeGone },
  });

  return { ...result, data: result.data ?? NO_EVENTS };
};

/** Refetches the sources and their events, e.g. after a refresh. */
export const useInvalidateSources = () => {
  const queryClient = useQueryClient();

  return useCallback(() => {
    queryClient.invalidateQueries({
      predicate: ({ queryKey }) =>
        queryKey[0] === PATH ||
        /^\/source\/\d+\/events$/.test(String(queryKey[0])),
    });
  }, [queryClient]);
};

export const useDeleteSource = (id: number) => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error, reset } = useApiMutation<void, void>({
    path: `${PATH}/${id}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.setQueryData<Source[]>([PATH], (oldData = []) =>
          oldData.filter((item) => item.id !== id)
        );
      },
    },
  });

  return {
    deleteSource: mutate,
    isDeleting: isPending,
    deleteError: error,
    resetDeleteError: reset,
  };
};

/** Only the fields' name and value are read back, like Sonarr's providers. */
type SourceRequest = Omit<Source, 'fields'> & {
  fields: { name: string; value: unknown }[];
};

function toRequest(
  source: Source,
  changedFields: Map<string, unknown>
): SourceRequest {
  return {
    ...source,
    fields: source.fields.map((field) => ({
      name: field.name,
      value: changedFields.has(field.name)
        ? changedFields.get(field.name)
        : field.value,
    })),
  };
}

/** The pending state of a new (from the selected type's schema) or existing source. */
export const useManageSource = (
  id: number | undefined,
  type: string | undefined,
  onSaved: (source: Source) => void
) => {
  const queryClient = useQueryClient();
  const { data: sources } = useSources();
  const { data: schema } = useSourceSchema(!id);

  const source = useMemo(() => {
    if (id) {
      return sources.find((s) => s.id === id);
    }

    const selected = schema.find((s) => s.type === type);

    return selected ? { ...selected, id: 0 } : undefined;
  }, [id, type, sources, schema]);

  const { pendingChanges, setPendingChange } = usePendingChangesStore<Source>(
    {}
  );
  const { pendingFields, setPendingFields } = usePendingFieldsStore();

  const save = useMutation<Source, ApiError, SourceRequest>({
    mutationFn: (data) =>
      fetchJson<Source, SourceRequest>({
        path: getQueryPath(data.id ? `${PATH}/${data.id}` : PATH),
        method: data.id ? 'PUT' : 'POST',
        headers,
        body: data,
      }),
    onSuccess: (saved) => {
      queryClient.setQueryData<Source[]>([PATH], (oldData = []) =>
        addOrUpdateQueryClientItem(oldData, saved, 'id')
      );
      onSaved(saved);
    },
  });

  const test = useMutation<SourceTestResult, ApiError, SourceRequest>({
    mutationFn: (data) =>
      fetchJson<SourceTestResult, SourceRequest>({
        path: getQueryPath(`${PATH}/test`),
        method: 'POST',
        headers,
        body: data,
      }),
  });

  const mutationError =
    save.submittedAt >= test.submittedAt ? save.error : test.error;

  const changedValues = useMemo(() => {
    const changed: Partial<Source> = {};

    (Object.keys(pendingChanges) as (keyof Source)[]).forEach((key) => {
      if (source?.[key] !== pendingChanges[key]) {
        // @ts-expect-error - the value matches the key
        changed[key] = pendingChanges[key];
      }
    });

    return changed;
  }, [source, pendingChanges]);

  const changedFields = useMemo(() => {
    const changed = new Map<string, unknown>();

    pendingFields.forEach((value, name) => {
      if (source?.fields.find((f) => f.name === name)?.value !== value) {
        changed.set(name, value);
      }
    });

    return changed;
  }, [source, pendingFields]);

  const { settings: item, ...settings } = useMemo(() => {
    const combinedPendingChanges = changedFields.size
      ? { ...changedValues, fields: Object.fromEntries(changedFields) }
      : changedValues;

    return selectSettings(source ?? {}, combinedPendingChanges, mutationError);
  }, [source, changedValues, changedFields, mutationError]);

  const current = useCallback(() => {
    if (!source) {
      throw new Error(`Source ${id ?? type} not found`);
    }

    return toRequest({ ...source, ...changedValues }, changedFields);
  }, [id, type, source, changedValues, changedFields]);

  const { mutate: saveMutate } = save;
  const { mutate: testMutate } = test;

  const saveSource = useCallback(
    () => saveMutate(current()),
    [current, saveMutate]
  );
  const testSource = useCallback(
    () => testMutate(current()),
    [current, testMutate]
  );

  return {
    ...settings,
    source,
    item: item as PendingSection<Source>,
    updateValue: setPendingChange,
    updateFieldValue: setPendingFields,
    saveSource,
    isSaving: save.isPending,
    saveError: mutationError,
    testSource,
    isTesting: test.isPending,
    testError: test.error,
    testResult: test.data,
  };
};
