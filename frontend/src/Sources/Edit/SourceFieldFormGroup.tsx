// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Components/Form/ProviderFieldFormGroup.tsx
// Maps Generral's source field types (including multi-line textArea) to inputs.
import React, { useMemo } from 'react';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { InputType } from 'Helpers/Props/inputTypes';
import { SourceSelectOption } from 'Sources/Source';
import { InputChanged } from 'typings/inputs';
import { Failure } from 'typings/pending';

interface SourceFieldFormGroupProps {
  advancedSettings: boolean;
  name: string;
  label: string;
  helpText?: string;
  placeholder?: string;
  unit?: string;
  value?: unknown;
  type: string;
  advanced: boolean;
  pending: boolean;
  errors: Failure[];
  warnings: Failure[];
  selectOptions?: SourceSelectOption[];
  onChange: (change: InputChanged<unknown>) => void;
}

function getInputType(type: string): InputType {
  switch (type) {
    case 'checkbox':
      return 'check';
    case 'keyValueList':
      return 'keyValueList';
    case 'number':
      return 'number';
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

function SourceFieldFormGroup({
  advancedSettings,
  name,
  label,
  helpText,
  placeholder,
  unit,
  value,
  type,
  advanced,
  pending,
  errors,
  warnings,
  selectOptions,
  onChange,
}: SourceFieldFormGroupProps) {
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
        type={getInputType(type)}
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

export default SourceFieldFormGroup;
