import React from 'react';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { inputTypes } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { Preset } from './usePresets';
import styles from './AddSeries.module.css';

interface PresetStepProps {
  preset: Preset;
  values: Record<string, string>;
  onChange: (change: InputChanged<string>) => void;
}

/** The preset's parameters and secrets; empty values use the preset's defaults. */
function PresetStep({ preset, values, onChange }: PresetStepProps) {
  if (!preset.parameters.length) {
    return null;
  }

  return (
    <>
      <p className={styles.stepHelp}>{translate('AddSeriesPresetHelpText')}</p>
      <Form>
        {preset.parameters.map((parameter) => (
          <FormRow key={parameter.name}>
            <FormLabel>{parameter.label}</FormLabel>
            {preset.requiredSecrets.includes(parameter.name) ? (
              <FormInputHelpText text={translate('Required')} />
            ) : null}
            <FormInput
              type={parameter.secret ? inputTypes.PASSWORD : inputTypes.TEXT}
              name={parameter.name}
              value={values[parameter.name] ?? ''}
              placeholder={parameter.default ?? undefined}
              onChange={onChange}
            />
          </FormRow>
        ))}
      </Form>
    </>
  );
}

export default PresetStep;
