// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Helpers/Hooks/useAppPage.ts
// Translations are bundled and set synchronously at startup, so only the API data is awaited here.
import { useMemo } from 'react';
import { useUiSettings } from 'Settings/UI/useUiSettings';
import useSystemStatus from 'System/Status/useSystemStatus';

const useAppPage = () => {
  const { isFetched: isSystemStatusFetched, error: systemStatusError } =
    useSystemStatus();

  const { isFetched: isUiSettingsFetched, error: uiSettingsError } =
    useUiSettings();

  const isPopulated = isSystemStatusFetched && isUiSettingsFetched;

  const { hasError, errors } = useMemo(() => {
    return {
      hasError: !!(uiSettingsError || systemStatusError),
      errors: { uiSettingsError, systemStatusError },
    };
  }, [uiSettingsError, systemStatusError]);

  const isLocalStorageSupported = useMemo(() => {
    const key = 'generralTest';

    try {
      localStorage.setItem(key, key);
      localStorage.removeItem(key);

      return true;
    } catch {
      return false;
    }
  }, []);

  return useMemo(() => {
    return { errors, hasError, isLocalStorageSupported, isPopulated };
  }, [errors, hasError, isLocalStorageSupported, isPopulated]);
};

export default useAppPage;
