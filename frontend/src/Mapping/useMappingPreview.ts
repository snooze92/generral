import { keepPreviousData, useQuery } from '@tanstack/react-query';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import { MappingPreview, MappingPreviewRequest } from './Mapping';

const headers = {
  'X-Api-Key': window.Generral.apiKey,
  'X-Generral-Client': 'Generral',
};

/**
 * `POST /mapping/preview` for an unsaved definition, cached per request so that going back to an
 * already previewed definition is instant. The previous result is kept while the next one loads.
 */
const useMappingPreview = (request: MappingPreviewRequest | undefined) => {
  const result = useQuery<MappingPreview, ApiError>({
    queryKey: ['/mapping/preview', request],
    queryFn: ({ signal }) =>
      fetchJson<MappingPreview, MappingPreviewRequest>({
        path: getQueryPath('/mapping/preview'),
        method: 'POST',
        headers,
        body: request,
        signal,
      }),
    enabled: !!request?.definition.trim(),
    placeholderData: keepPreviousData,
    retry: false,
    staleTime: Infinity,
  });

  return result;
};

export default useMappingPreview;
