// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/General/SecuritySettings.tsx
// and FormsAuthenticationSettings.tsx
import React, { FocusEvent, useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import FormInput from 'Components/Form/FormInput';
import FormInputButton from 'Components/Form/FormInputButton';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import Icon from 'Components/Icon';
import ClipboardButton from 'Components/Link/ClipboardButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { icons, inputTypes, kinds } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import { PendingSection } from 'typings/pending';
import translate from 'Utilities/String/translate';
import {
  EnvironmentOverrideHelpText,
  useIsOverriddenByEnvironment,
} from './EnvironmentOverride';
import { GeneralSettingsModel } from './useGeneralSettings';

export const authenticationMethodOptions: EnhancedSelectInputValue<string>[] = [
  {
    key: 'forms',
    get value() {
      return translate('AuthForm');
    },
  },
  {
    key: 'external',
    get value() {
      return translate('External');
    },
  },
];

export const authenticationRequiredOptions: EnhancedSelectInputValue<string>[] =
  [
    {
      key: 'enabled',
      get value() {
        return translate('Enabled');
      },
    },
    {
      key: 'disabledForLocalAddresses',
      get value() {
        return translate('DisabledForLocalAddresses');
      },
    },
  ];

interface SecuritySettingsProps {
  authenticationMethod: PendingSection<GeneralSettingsModel>['authenticationMethod'];
  authenticationRequired: PendingSection<GeneralSettingsModel>['authenticationRequired'];
  username: PendingSection<GeneralSettingsModel>['username'];
  password: PendingSection<GeneralSettingsModel>['password'];
  passwordConfirmation: PendingSection<GeneralSettingsModel>['passwordConfirmation'];
  apiKey: PendingSection<GeneralSettingsModel>['apiKey'];
  isResettingApiKey: boolean;
  onInputChange: (change: InputChanged) => void;
  onResetApiKey: () => void;
}

function SecuritySettings({
  authenticationMethod,
  authenticationRequired,
  username,
  password,
  passwordConfirmation,
  apiKey,
  isResettingApiKey,
  onInputChange,
  onResetApiKey,
}: SecuritySettingsProps) {
  const [isConfirmApiKeyResetModalOpen, setIsConfirmApiKeyResetModalOpen] =
    useState(false);

  const isMethodFixed = useIsOverriddenByEnvironment('authenticationMethod');
  const isRequiredFixed = useIsOverriddenByEnvironment(
    'authenticationRequired'
  );
  const isApiKeyFixed = useIsOverriddenByEnvironment('apiKey');

  const handleApikeyFocus = useCallback(
    (event: FocusEvent<HTMLInputElement, Element>) => {
      event.target.select();
    },
    []
  );

  const handleResetApiKeyPress = useCallback(() => {
    setIsConfirmApiKeyResetModalOpen(true);
  }, []);

  const handleConfirmResetApiKey = useCallback(() => {
    setIsConfirmApiKeyResetModalOpen(false);
    onResetApiKey();
  }, [onResetApiKey]);

  const handleCloseResetApiKeyModal = useCallback(() => {
    setIsConfirmApiKeyResetModalOpen(false);
  }, []);

  return (
    <FieldSet
      legend={translate('Security')}
      caption={translate('SecurityCaption')}
    >
      <FormRow>
        <FormLabel>{translate('Authentication')}</FormLabel>
        <FormInputHelpText text={translate('AuthenticationMethodHelpText')} />
        <EnvironmentOverrideHelpText name="authenticationMethod" />
        <FormInput
          type={inputTypes.SELECT}
          name="authenticationMethod"
          values={authenticationMethodOptions}
          isDisabled={isMethodFixed}
          onChange={onInputChange}
          {...authenticationMethod}
        />
      </FormRow>

      <FormRow>
        <FormLabel>{translate('AuthenticationRequired')}</FormLabel>
        <FormInputHelpText text={translate('AuthenticationRequiredHelpText')} />
        <EnvironmentOverrideHelpText name="authenticationRequired" />
        <FormInput
          type={inputTypes.SELECT}
          name="authenticationRequired"
          values={authenticationRequiredOptions}
          isDisabled={isRequiredFixed}
          onChange={onInputChange}
          {...authenticationRequired}
        />
      </FormRow>

      {authenticationMethod.value === 'forms' ? (
        <>
          <FormRow>
            <FormLabel>{translate('Username')}</FormLabel>
            <FormInput
              type={inputTypes.TEXT}
              name="username"
              autocomplete="username"
              onChange={onInputChange}
              {...username}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('Password')}</FormLabel>
            <FormInputHelpText text={translate('PasswordUnchangedHelpText')} />
            <FormInput
              type={inputTypes.PASSWORD}
              name="password"
              autocomplete="new-password"
              onChange={onInputChange}
              {...password}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('PasswordConfirmation')}</FormLabel>
            <FormInput
              type={inputTypes.PASSWORD}
              name="passwordConfirmation"
              autocomplete="new-password"
              onChange={onInputChange}
              {...passwordConfirmation}
            />
          </FormRow>
        </>
      ) : null}

      <FormRow>
        <FormLabel>{translate('ApiKey')}</FormLabel>
        <FormInputHelpText
          text={translate('BrowserReloadRequired')}
          isWarning={true}
        />
        <EnvironmentOverrideHelpText name="apiKey" />
        <FormInput
          type={inputTypes.TEXT}
          name="apiKey"
          readOnly={true}
          buttons={[
            <ClipboardButton
              key="copy"
              value={apiKey.value}
              kind={kinds.DEFAULT}
            />,

            isApiKeyFixed ? null : (
              <FormInputButton
                key="reset"
                kind={kinds.DANGER}
                title={translate('ResetAPIKey')}
                aria-label={translate('ResetAPIKey')}
                onPress={handleResetApiKeyPress}
              >
                <Icon name={icons.REFRESH} isSpinning={isResettingApiKey} />
              </FormInputButton>
            ),
          ]}
          onChange={onInputChange}
          onFocus={handleApikeyFocus}
          {...apiKey}
        />
      </FormRow>

      <ConfirmModal
        isOpen={isConfirmApiKeyResetModalOpen}
        kind={kinds.DANGER}
        title={translate('ResetAPIKey')}
        message={translate('ResetAPIKeyMessageText')}
        confirmLabel={translate('Reset')}
        onConfirm={handleConfirmResetApiKey}
        onCancel={handleCloseResetApiKeyModal}
      />
    </FieldSet>
  );
}

export default SecuritySettings;
