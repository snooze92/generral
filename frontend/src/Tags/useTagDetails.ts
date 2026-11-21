// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Tags/useTagDetails.ts
import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import ModelBase from 'App/ModelBase';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

export const PATH = '/tag/detail';

const DEFAULT_TAG_DETAILS: TagDetail[] = [];

export interface TagDetail extends ModelBase {
  label: string;
  sourceIds: number[];
  seriesIds: number[];
}

const useTagDetails = () => {
  const { queryKey, ...result } = useApiQuery<TagDetail[]>({ path: PATH });

  return {
    ...result,
    data: result.data ?? DEFAULT_TAG_DETAILS,
  };
};

export default useTagDetails;

export const useTagDetail = (id: number) => {
  const { data: tagDetails } = useTagDetails();

  return (
    tagDetails.find((tagDetail) => tagDetail.id === id) ?? {
      sourceIds: [],
      seriesIds: [],
    }
  );
};

/** Refetches the tag usage, e.g. after a tag was added, renamed or deleted. */
export const useInvalidateTagDetails = () => {
  const queryClient = useQueryClient();

  return useCallback(() => {
    queryClient.invalidateQueries({ queryKey: [PATH] });
  }, [queryClient]);
};
