import { useMutation, useQueryClient } from '@tanstack/react-query';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Series from 'Series/Series';
import { PATH as SERIES_PATH } from 'Series/useSeries';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import { NewSeries } from './useAddSeries';

export interface PresetParameter {
  name: string;
  label: string;
  default?: string | null;
  secret: boolean;
}

export interface Preset {
  id: string;
  name: string;
  description?: string | null;
  seriesTitle?: string | null;
  tags: string[];
  sourceType: string;
  requiredSecrets: string[];
  parameters: PresetParameter[];
  shipped: boolean;
}

export interface InstantiateRequest
  extends Pick<
    NewSeries,
    'title' | 'rootFolderPath' | 'monitored' | 'monitorNewItems' | 'seasonFolder' | 'qualityProfileId' | 'tags'
  > {
  parameters: Record<string, string>;
  secrets: Record<string, string>;
}

const NO_PRESETS: Preset[] = [];

const headers = {
  'X-Api-Key': window.Generral.apiKey,
  'X-Generral-Client': 'Generral',
};

export const usePresets = () => {
  const result = useApiQuery<Preset[]>({ path: '/preset' });

  return { ...result, data: result.data ?? NO_PRESETS };
};

/** POST /preset/import from a URL or a YAML document; resolves to the stored preset. */
export const useImportPreset = () => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error, reset } = useMutation<
    Preset,
    ApiError,
    { url?: string; yaml?: string }
  >({
    mutationFn: (body) =>
      fetchJson<Preset, { url?: string; yaml?: string }>({
        path: getQueryPath('/preset/import'),
        method: 'POST',
        headers,
        body,
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['/preset'] }),
  });

  return { importPreset: mutate, isImporting: isPending, importError: error, reset };
};

/** POST /preset/{id}/instantiate: creates the source(s), series and mapping(s), and queues a refresh. */
export const useInstantiatePreset = () => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error, reset } = useMutation<
    Series,
    ApiError,
    { id: string; request: InstantiateRequest }
  >({
    mutationFn: ({ id, request }) =>
      fetchJson<Series, InstantiateRequest>({
        path: getQueryPath(`/preset/${encodeURIComponent(id)}/instantiate`),
        method: 'POST',
        headers,
        body: request,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [SERIES_PATH] });
      queryClient.invalidateQueries({ queryKey: ['/source'] });
    },
  });

  return { instantiate: mutate, isInstantiating: isPending, instantiateError: error, reset };
};
