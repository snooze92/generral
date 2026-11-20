// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/General/useGeneralSettings.ts
import { useQueryClient } from '@tanstack/react-query';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import { useManageSettings, useSettings } from 'Settings/useSettings';

export interface GeneralSettingsModel {
  bindAddress: string;
  port: number;
  urlBase: string;
  instanceName: string;
  authenticationMethod: 'forms' | 'external';
  authenticationRequired: 'enabled' | 'disabledForLocalAddresses';
  username: string;
  password: string;
  passwordConfirmation: string;
  apiKey: string;
  logLevel: 'info' | 'debug' | 'trace';
  allowScriptSources: boolean;
  overriddenByEnvironment: string[];
}

const PATH = '/settings/general';

export const useGeneralSettings = () => {
  return useSettings<GeneralSettingsModel>(PATH);
};

export const useManageGeneralSettings = () => {
  return useManageSettings<GeneralSettingsModel>(PATH);
};

export const useResetApiKey = () => {
  const queryClient = useQueryClient();

  return useApiMutation<GeneralSettingsModel, void>({
    method: 'POST',
    path: `${PATH}/resetapikey`,
    mutationOptions: {
      onSuccess: (settings) => {
        queryClient.setQueryData([PATH], settings);
      },
    },
  });
};
