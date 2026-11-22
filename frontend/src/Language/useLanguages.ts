// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Language/useLanguages.ts
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Language from 'Language/Language';

const NO_LANGUAGES: Language[] = [];

/** Sonarr's languages plus `Any` (-1) and `Original` (-2). */
const useLanguages = () => {
  const { data, ...result } = useApiQuery<Language[]>({
    path: '/language',
    queryOptions: { gcTime: Infinity, staleTime: Infinity },
  });

  return { ...result, data: data ?? NO_LANGUAGES };
};

export default useLanguages;
