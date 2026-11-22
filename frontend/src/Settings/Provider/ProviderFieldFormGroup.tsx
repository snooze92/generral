// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Components/Form/ProviderFieldFormGroup.tsx
// Maps the field types of sources (including multi-line textArea), indexers and download clients
// to inputs.
import React, { useMemo } from 'react';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { InputType } from 'Helpers/Props/inputTypes';
import { InputChanged } from 'typings/inputs';
import { Failure } from 'typings/pending';
import { ProviderSelectOption } from './Provider';

interface ProviderFieldFormGroupProps {
  advancedSettings: boolean;
  name: string;
  label: string;
  helpText?: string;
  placeholder?: string;
  unit?: string;
  value?: unknown;
  type: string;
  isFloat?: boolean;
  advanced: boolean;
  pending: boolean;
  errors: Failure[];
  warnings: Failure[];
  selectOptions?: ProviderSelectOption[];
  onChange: (change: InputChanged<unknown>) => void;
}

function getInputType(type: string, isFloat?: boolean): InputType {
  switch (type) {
    case 'checkbox':
      return 'check';
    case 'keyValueList':
      return 'keyValueList';
    case 'number':
      return isFloat ? 'float' : 'number';
    case 'password':
      return 'password';
    case 'select':
      return 'select';
    case 'textArea':
      return 'textArea';
    default:
      return 'text';
  }
}

function ProviderFieldFormGroup({
  advancedSettings,
  name,
  label,
  helpText,
  placeholder,
  unit,
  value,
  type,
  isFloat,
  advanced,
  pending,
  errors,
  warnings,
  selectOptions,
  onChange,
}: ProviderFieldFormGroupProps) {
  const selectValues = useMemo(() => {
    return selectOptions?.map((option) => ({
      key: option.value,
      value: option.name,
      hint: option.hint,
    }));
  }, [selectOptions]);

  return (
    <FormRow advancedSettings={advancedSettings} isAdvanced={advanced}>
      <FormLabel>{label}</FormLabel>
      <FormInputHelpText text={helpText} />
      <FormInput
        type={getInputType(type, isFloat)}
        name={name}
        placeholder={placeholder}
        unit={unit}
        value={value ?? undefined}
        // @ts-expect-error - only select inputs take values
        values={selectValues}
        errors={errors}
        warnings={warnings}
        pending={pending}
        onChange={onChange}
      />
    </FormRow>
  );
}

export default ProviderFieldFormGroup;
