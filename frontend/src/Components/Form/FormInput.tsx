// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Components/Form/FormInput.tsx
// Only the inputs Generral ships are mapped; other input types fall back to a text input.
import classNames from 'classnames';
import React, { ElementType, ReactNode, useMemo, useState } from 'react';
import { inputTypes } from 'Helpers/Props';
import { InputType } from 'Helpers/Props/inputTypes';
import { ValidationError, ValidationWarning } from 'typings/pending';
import AutoCompleteInput, { AutoCompleteInputProps } from './AutoCompleteInput';
import CheckInput, { CheckInputProps } from './CheckInput';
import FloatInput, { FloatInputProps } from './FloatInput';
import { FormInputButtonProps } from './FormInputButton';
import { FormInputGroupProvider } from './FormInputGroupContext';
import FormInputHelpText from './FormInputHelpText';
import { useFormRow } from './FormRowContext';
import KeyValueListInput, { KeyValueListInputProps } from './KeyValueListInput';
import NumberInput, { NumberInputProps } from './NumberInput';
import PasswordInput from './PasswordInput';
import EnhancedSelectInput, {
  EnhancedSelectInputProps,
} from './Select/EnhancedSelectInput';
import UMaskInput, { UMaskInputProps } from './Select/UMaskInput';
import SeriesTagInput, { SeriesTagInputProps } from './Tag/SeriesTagInput';
import TagSelectInput, { TagSelectInputProps } from './Tag/TagSelectInput';
import TextTagInput, { TextTagInputProps } from './Tag/TextTagInput';
import TextArea, { TextAreaProps } from './TextArea';
import TextInput, { TextInputProps } from './TextInput';
import styles from './FormInput.module.css';

const componentMap: Partial<Record<InputType, ElementType>> = {
  autoComplete: AutoCompleteInput,
  check: CheckInput,
  date: TextInput,
  float: FloatInput,
  keyValueList: KeyValueListInput,
  number: NumberInput,
  password: PasswordInput,
  select: EnhancedSelectInput,
  tag: SeriesTagInput,
  tagSelect: TagSelectInput,
  text: TextInput,
  textArea: TextArea,
  textTag: TextTagInput,
  umask: UMaskInput,
};


type PickProps<V, C extends InputType> = C extends 'autoComplete'
  ? AutoCompleteInputProps
  : C extends 'check'
  ? CheckInputProps
  : C extends 'float'
  ? FloatInputProps
  : C extends 'keyValueList'
  ? KeyValueListInputProps
  : C extends 'number'
  ? NumberInputProps
  : C extends 'select'
  ? // eslint-disable-next-line @typescript-eslint/no-explicit-any
    EnhancedSelectInputProps<any, V>
  : C extends 'tag'
  ? SeriesTagInputProps<V>
  : C extends 'tagSelect'
  ? TagSelectInputProps
  : C extends 'textArea'
  ? TextAreaProps
  : C extends 'textTag'
  ? TextTagInputProps
  : C extends 'umask'
  ? UMaskInputProps
  : TextInputProps;

export interface ValidationMessage {
  message: string;
}

// 'type' is omitted too: fallback types (e.g. 'path') would otherwise clash with TextInput's own type prop.
export type FormInputProps<V, C extends InputType> = Omit<
  PickProps<V, C>,
  'className' | 'type'
> & {
  type: C;
  id?: string;
  className?: string;
  containerClassName?: string;
  inputClassName?: string;
  autoFocus?: boolean;
  autocomplete?: string;
  name: string;
  buttons?: ReactNode | ReactNode[];
  pending?: boolean;
  placeholder?: string;
  unit?: string;
  errors?: (ValidationMessage | ValidationError)[];
  warnings?: (ValidationMessage | ValidationWarning)[];
};

function FormInput<T, C extends InputType>(props: FormInputProps<T, C>) {
  const formRow = useFormRow();
  const {
    className = styles.inputGroup,
    containerClassName = styles.inputGroupContainer,
    inputClassName,
    type,
    id = formRow?.id ?? props.name,
    name,
    unit,
    buttons = [],
    pending,
    errors: serverErrors = [],
    warnings: serverWarnings = [],
    ...otherProps
  } = props;

  const [clientErrors, setClientErrors] = useState<
    (ValidationMessage | ValidationError)[]
  >([]);

  const [clientWarnings, setClientWarnings] = useState<
    (ValidationMessage | ValidationWarning)[]
  >([]);

  const errors = useMemo(() => {
    return [...clientErrors, ...serverErrors];
  }, [clientErrors, serverErrors]);

  const warnings = useMemo(() => {
    return [...clientWarnings, ...serverWarnings];
  }, [clientWarnings, serverWarnings]);

  const InputComponent: ElementType = componentMap[type] ?? TextInput;
  const checkInput = type === inputTypes.CHECK;
  const hasError = !!errors.length;
  const hasWarning = !hasError && !!warnings.length;
  const describedBy = formRow?.descriptionIds.join(' ') || undefined;
  const buttonsArray = React.Children.toArray(buttons);
  const lastButtonIndex = buttonsArray.length - 1;
  const hasButton = !!buttonsArray.length;

  return (
    <FormInputGroupProvider
      describedBy={describedBy}
      hasError={hasError}
      setClientErrors={setClientErrors}
      setClientWarnings={setClientWarnings}
    >
      <div className={containerClassName}>
        <div
          className={classNames(
            className,
            checkInput && styles.checkInput,
            hasButton && styles.hasButtons,
            hasButton && hasError && styles.hasError,
            hasButton && hasWarning && styles.hasWarning
          )}
        >
          <div className={styles.inputContainer}>
            <InputComponent
              id={id}
              name={name}
              className={inputClassName}
              hasError={hasError}
              hasWarning={hasWarning}
              hasButton={hasButton}
              {...otherProps}
            />

            {unit && (
              <div
                className={
                  type === inputTypes.NUMBER || type === inputTypes.FLOAT
                    ? styles.inputUnitNumber
                    : styles.inputUnit
                }
              >
                {unit}
              </div>
            )}
          </div>

          {buttonsArray.map((button, index) => {
            if (!React.isValidElement<FormInputButtonProps>(button)) {
              return button;
            }

            return React.cloneElement(button, {
              isLastButton: index === lastButtonIndex,
            });
          })}
        </div>

        {errors.map((error, index) => {
          return 'errorMessage' in error ? (
            <FormInputHelpText
              key={index}
              text={error.errorMessage}
              link={error.infoLink}
              tooltip={error.detailedDescription}
              isError={true}
              isCheckInput={checkInput}
            />
          ) : (
            <FormInputHelpText
              key={index}
              text={error.message}
              isError={true}
              isCheckInput={checkInput}
            />
          );
        })}

        {warnings.map((warning, index) => {
          return 'errorMessage' in warning ? (
            <FormInputHelpText
              key={index}
              text={warning.errorMessage}
              link={warning.infoLink}
              tooltip={warning.detailedDescription}
              isWarning={true}
              isCheckInput={checkInput}
            />
          ) : (
            <FormInputHelpText
              key={index}
              text={warning.message}
              isWarning={true}
              isCheckInput={checkInput}
            />
          );
        })}
      </div>
    </FormInputGroupProvider>
  );
}

export default FormInput;
