// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/General/LoggingSettings.tsx
import React from 'react';
import FieldSet from 'Components/FieldSet';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import { inputTypes } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import { PendingSection } from 'typings/pending';
import translate from 'Utilities/String/translate';
import {
  EnvironmentOverrideHelpText,
  useIsOverriddenByEnvironment,
} from './EnvironmentOverride';
import { GeneralSettingsModel } from './useGeneralSettings';

const logLevelOptions: EnhancedSelectInputValue<string>[] = [
  {
    key: 'info',
    get value() {
      return translate('Info');
    },
  },
  {
    key: 'debug',
    get value() {
      return translate('Debug');
    },
  },
  {
    key: 'trace',
    get value() {
      return translate('Trace');
    },
  },
];

interface LoggingSettingsProps {
  logLevel: PendingSection<GeneralSettingsModel>['logLevel'];
  onInputChange: (change: InputChanged) => void;
}

function LoggingSettings({ logLevel, onInputChange }: LoggingSettingsProps) {
  const isLogLevelFixed = useIsOverriddenByEnvironment('logLevel');

  return (
    <FieldSet
      legend={translate('Logging')}
      caption={translate('LoggingCaption')}
    >
      <FormRow>
        <FormLabel>{translate('LogLevel')}</FormLabel>
        <FormInputHelpText
          text={
            logLevel.value === 'trace'
              ? translate('LogLevelTraceHelpTextWarning')
              : undefined
          }
          isWarning={true}
        />
        <EnvironmentOverrideHelpText name="logLevel" />
        <FormInput
          type={inputTypes.SELECT}
          name="logLevel"
          values={logLevelOptions}
          isDisabled={isLogLevelFixed}
          onChange={onInputChange}
          {...logLevel}
        />
      </FormRow>
    </FieldSet>
  );
}

export default LoggingSettings;
