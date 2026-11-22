import ModelBase from 'App/ModelBase';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Language from 'Language/Language';

export interface Quality {
  id: number;
  name: string;
}

export interface QualityProfileItem {
  /** Set for groups (numbered from 1000), which have `name` and `items` instead of `quality`. */
  id?: number;
  name?: string;
  quality?: Quality;
  items: QualityProfileItem[];
  allowed: boolean;
}

export interface QualityProfile extends ModelBase {
  name: string;
  upgradeAllowed: boolean;
  cutoff: number;
  language: Language;
  /** Lowest to highest preference. */
  items: QualityProfileItem[];
}

export const PATH = '/qualityprofile';

const DEFAULT_PROFILES: QualityProfile[] = [];

const useQualityProfiles = () => {
  const { data, ...result } = useApiQuery<QualityProfile[]>({
    path: PATH,
    queryOptions: { staleTime: 30 * 1000 },
  });

  return { ...result, data: data ?? DEFAULT_PROFILES };
};

export const useQualityProfileSchema = (enabled: boolean) =>
  useApiQuery<QualityProfile>({
    path: `${PATH}/schema`,
    queryOptions: { enabled },
  });

export default useQualityProfiles;
