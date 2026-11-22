import ModelBase from 'App/ModelBase';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

export interface QualityProfile extends ModelBase {
  name: string;
  upgradeAllowed: boolean;
  cutoff: number;
}

const DEFAULT_PROFILES: QualityProfile[] = [];

const useQualityProfiles = () => {
  const { data, ...result } = useApiQuery<QualityProfile[]>({
    path: '/qualityprofile',
    queryOptions: { staleTime: 30 * 1000 },
  });

  return { ...result, data: data ?? DEFAULT_PROFILES };
};

export default useQualityProfiles;
