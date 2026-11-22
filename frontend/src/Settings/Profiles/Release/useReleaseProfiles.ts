import ModelBase from 'App/ModelBase';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

export interface ReleaseProfile extends ModelBase {
  name: string;
  enabled: boolean;
  required: string[];
  ignored: string[];
  /** 0 = any indexer. */
  indexerId: number;
  tags: number[];
  excludedTags: number[];
}

export const PATH = '/releaseprofile';

const NONE: ReleaseProfile[] = [];

const useReleaseProfiles = () => {
  const { data, ...result } = useApiQuery<ReleaseProfile[]>({ path: PATH });

  return { ...result, data: (data ?? NONE) as ReleaseProfile[] };
};

export default useReleaseProfiles;
