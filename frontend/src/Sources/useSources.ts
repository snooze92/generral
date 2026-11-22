// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/useProviderSettings.ts
import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import {
  useDeleteProvider,
  useManageProvider,
  useProviders,
  useProviderSchema,
} from 'Settings/Provider/useProviders';
import Source, { SourceEvent, SourceTestResult } from './Source';

export const PATH = '/source';

const NO_EVENTS: SourceEvent[] = [];

export const useSources = () => useProviders<Source>(PATH);

export const useSource = (id: number | undefined) => {
  const { data, ...result } = useSources();

  return { ...result, data: data.find((s) => s.id === id) };
};

export const useSourceSchema = (enabled = true) =>
  useProviderSchema<Source>(PATH, enabled);

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
  const { deleteProvider, ...result } = useDeleteProvider<Source>(PATH, id);

  return { ...result, deleteSource: deleteProvider };
};

/** The pending state of a new (from the selected type's schema) or existing source. */
export const useManageSource = (
  id: number | undefined,
  type: string | undefined,
  onSaved: (source: Source) => void
) => {
  const selectSchema = useCallback(
    (schema: Source[]) => schema.find((s) => s.type === type),
    [type]
  );

  const { provider, saveProvider, testProvider, ...result } = useManageProvider<
    Source,
    SourceTestResult
  >({ path: PATH, id, selectSchema, onSaved });

  return {
    ...result,
    source: provider,
    saveSource: saveProvider,
    testSource: testProvider,
  };
};
