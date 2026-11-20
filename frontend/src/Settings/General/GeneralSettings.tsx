// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/General/GeneralSettings.tsx
import React, { useCallback, useEffect, useRef, useState } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import settingsStyles from 'Settings/Settings.module.css';
import SettingsPage from 'Settings/SettingsPage';
import { useRestart } from 'System/useSystem';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { EnvironmentOverrideProvider } from './EnvironmentOverride';
import HostSettings from './HostSettings';
import LoggingSettings from './LoggingSettings';
import SecuritySettings from './SecuritySettings';
import { useManageGeneralSettings, useResetApiKey } from './useGeneralSettings';

const requiresRestartKeys = ['bindAddress', 'port', 'urlBase', 'instanceName'];

const noOverrides: string[] = [];

function GeneralSettings() {
  const { mutate: restart } = useRestart();
  const { mutate: resetApiKey, isPending: isResettingApiKey } = useResetApiKey();

  const {
    settings,
    isFetching,
    isFetched,
    error,
    updateSetting,
    saveSettings,
    isSaving,
    saveError,
    hasPendingChanges,
    hasSettings,
    pendingChanges,
    validationErrors,
    validationWarnings,
  } = useManageGeneralSettings();

  const wasSaving = usePrevious(isSaving);
  const isRestartRequired = useRef(false);

  const [isRestartRequiredModalOpen, setIsRestartRequiredModalOpen] =
    useState(false);

  const handleInputChange = useCallback(
    (change: InputChanged) => {
      // @ts-expect-error input change events aren't typed
      updateSetting(change.name, change.value);
    },
    [updateSetting]
  );

  const handleSavePress = useCallback(() => {
    isRestartRequired.current = Object.keys(pendingChanges ?? {}).some(
      (key) => {
        return requiresRestartKeys.includes(key);
      }
    );

    saveSettings();
  }, [pendingChanges, saveSettings]);

  const handleResetApiKey = useCallback(() => {
    resetApiKey(undefined, {
      onSuccess: () => {
        location.reload();
      },
    });
  }, [resetApiKey]);

  const handleConfirmRestart = useCallback(() => {
    setIsRestartRequiredModalOpen(false);
    restart();
  }, [restart]);

  const handleCloseRestartRequiredModal = useCallback(() => {
    setIsRestartRequiredModalOpen(false);
  }, []);

  useEffect(() => {
    if (!isSaving && wasSaving && !saveError && isRestartRequired.current) {
      isRestartRequired.current = false;
      setIsRestartRequiredModalOpen(true);
    }
  }, [isSaving, wasSaving, saveError]);

  const overriddenByEnvironment =
    settings.overriddenByEnvironment?.value ?? noOverrides;

  return (
    <SettingsPage
      title={translate('GeneralSettings')}
      hasPendingChanges={hasPendingChanges}
      isSaving={isSaving}
      onSavePress={handleSavePress}
    >
      <PageContentBody>
        <div className={settingsStyles.section}>
          <PageHeading
            scope={translate('Settings')}
            title={translate('General')}
          />

          {isFetching && !isFetched ? <LoadingIndicator /> : null}

          {!isFetching && error ? (
            <Alert kind={kinds.DANGER}>
              {translate('GeneralSettingsLoadError')}
            </Alert>
          ) : null}

          {hasSettings && isFetched && !error ? (
            <EnvironmentOverrideProvider value={overriddenByEnvironment}>
              <Form
                id="generalSettings"
                validationErrors={validationErrors}
                validationWarnings={validationWarnings}
              >
                <HostSettings
                  bindAddress={settings.bindAddress}
                  port={settings.port}
                  urlBase={settings.urlBase}
                  instanceName={settings.instanceName}
                  onInputChange={handleInputChange}
                />

                <SecuritySettings
                  authenticationMethod={settings.authenticationMethod}
                  authenticationRequired={settings.authenticationRequired}
                  username={settings.username}
                  password={settings.password}
                  passwordConfirmation={settings.passwordConfirmation}
                  apiKey={settings.apiKey}
                  isResettingApiKey={isResettingApiKey}
                  onInputChange={handleInputChange}
                  onResetApiKey={handleResetApiKey}
                />

                <LoggingSettings
                  logLevel={settings.logLevel}
                  onInputChange={handleInputChange}
                />

                <FieldSet
                  legend={translate('Sources')}
                  caption={translate('SourcesSettingsCaption')}
                >
                  <FormRow size={sizes.MEDIUM}>
                    <FormLabel>{translate('AllowScriptSources')}</FormLabel>
                    <FormInputHelpText
                      text={translate('AllowScriptSourcesHelpText')}
                    />
                    <FormInput
                      type={inputTypes.CHECK}
                      name="allowScriptSources"
                      isDisabled={true}
                      onChange={handleInputChange}
                      {...settings.allowScriptSources}
                    />
                  </FormRow>
                </FieldSet>
              </Form>
            </EnvironmentOverrideProvider>
          ) : null}
        </div>
      </PageContentBody>

      <ConfirmModal
        isOpen={isRestartRequiredModalOpen}
        kind={kinds.DANGER}
        title={translate('RestartApp')}
        message={translate('RestartRequiredToApplyChanges')}
        cancelLabel={translate('RestartLater')}
        confirmLabel={translate('RestartNow')}
        onConfirm={handleConfirmRestart}
        onCancel={handleCloseRestartRequiredModal}
      />
    </SettingsPage>
  );
}

export default GeneralSettings;
