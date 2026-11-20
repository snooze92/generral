// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/General/HostSettings.tsx
import React from 'react';
import FieldSet from 'Components/FieldSet';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { inputTypes } from 'Helpers/Props';
import { useShowAdvancedSettings } from 'Settings/advancedSettingsStore';
import { InputChanged } from 'typings/inputs';
import { PendingSection } from 'typings/pending';
import translate from 'Utilities/String/translate';
import {
  EnvironmentOverrideHelpText,
  useIsOverriddenByEnvironment,
} from './EnvironmentOverride';
import { GeneralSettingsModel } from './useGeneralSettings';

interface HostSettingsProps {
  bindAddress: PendingSection<GeneralSettingsModel>['bindAddress'];
  port: PendingSection<GeneralSettingsModel>['port'];
  urlBase: PendingSection<GeneralSettingsModel>['urlBase'];
  instanceName: PendingSection<GeneralSettingsModel>['instanceName'];
  onInputChange: (change: InputChanged) => void;
}

function HostSettings({
  bindAddress,
  port,
  urlBase,
  instanceName,
  onInputChange,
}: HostSettingsProps) {
  const showAdvancedSettings = useShowAdvancedSettings();
  const isBindAddressFixed = useIsOverriddenByEnvironment('bindAddress');
  const isPortFixed = useIsOverriddenByEnvironment('port');
  const isUrlBaseFixed = useIsOverriddenByEnvironment('urlBase');
  const isInstanceNameFixed = useIsOverriddenByEnvironment('instanceName');

  return (
    <FieldSet legend={translate('Host')} caption={translate('HostCaption')}>
      <FormRow advancedSettings={showAdvancedSettings} isAdvanced={true}>
        <FormLabel>{translate('BindAddress')}</FormLabel>
        <FormInputHelpText text={translate('BindAddressHelpText')} />
        <FormInputHelpText
          text={translate('RestartRequiredHelpTextWarning')}
          isWarning={true}
        />
        <EnvironmentOverrideHelpText name="bindAddress" />
        <FormInput
          type={inputTypes.TEXT}
          name="bindAddress"
          readOnly={isBindAddressFixed}
          onChange={onInputChange}
          {...bindAddress}
        />
      </FormRow>

      <FormRow>
        <FormLabel>{translate('PortNumber')}</FormLabel>
        <FormInputHelpText
          text={translate('RestartRequiredHelpTextWarning')}
          isWarning={true}
        />
        <EnvironmentOverrideHelpText name="port" />
        <FormInput
          type={inputTypes.NUMBER}
          name="port"
          min={1}
          max={65535}
          autocomplete="off"
          readOnly={isPortFixed}
          onChange={onInputChange}
          {...port}
        />
      </FormRow>

      <FormRow>
        <FormLabel>{translate('UrlBase')}</FormLabel>
        <FormInputHelpText text={translate('UrlBaseHelpText')} />
        <FormInputHelpText
          text={translate('RestartRequiredHelpTextWarning')}
          isWarning={true}
        />
        <EnvironmentOverrideHelpText name="urlBase" />
        <FormInput
          type={inputTypes.TEXT}
          name="urlBase"
          readOnly={isUrlBaseFixed}
          onChange={onInputChange}
          {...urlBase}
        />
      </FormRow>

      <FormRow>
        <FormLabel>{translate('InstanceName')}</FormLabel>
        <FormInputHelpText text={translate('InstanceNameHelpText')} />
        <FormInputHelpText
          text={translate('RestartRequiredHelpTextWarning')}
          isWarning={true}
        />
        <EnvironmentOverrideHelpText name="instanceName" />
        <FormInput
          type={inputTypes.TEXT}
          name="instanceName"
          readOnly={isInstanceNameFixed}
          onChange={onInputChange}
          {...instanceName}
        />
      </FormRow>
    </FieldSet>
  );
}

export default HostSettings;
