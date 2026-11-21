// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Indexers/Indexers/EditIndexerModalContent.tsx
import React, { useCallback } from 'react';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds } from 'Helpers/Props';
import AdvancedSettingsButton from 'Settings/AdvancedSettingsButton';
import { useShowAdvancedSettings } from 'Settings/advancedSettingsStore';
import Source from 'Sources/Source';
import { useManageSource } from 'Sources/useSources';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import SourceFieldFormGroup from './SourceFieldFormGroup';
import SourcePreview from './SourcePreview';
import styles from './EditSourceModalContent.module.css';

export interface EditSourceModalContentProps {
  id?: number;
  type?: string;
  onModalClose: () => void;
  onDeletePress?: () => void;
}

function EditSourceModalContent({
  id,
  type,
  onModalClose,
  onDeletePress,
}: EditSourceModalContentProps) {
  const showAdvancedSettings = useShowAdvancedSettings();

  const {
    source,
    item,
    updateValue,
    updateFieldValue,
    saveSource,
    isSaving,
    saveError,
    testSource,
    isTesting,
    testError,
    testResult,
    validationErrors,
    validationWarnings,
  } = useManageSource(id, type, onModalClose);

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      updateValue(name as keyof Source, value as never);
    },
    [updateValue]
  );

  const handleFieldChange = useCallback(
    ({ name, value }: InputChanged<unknown>) => {
      updateFieldValue({ [name]: value });
    },
    [updateFieldValue]
  );

  if (!source) {
    return null;
  }

  const { name, enabled, refreshInterval, fields } = item;
  const typeName = source.typeName ?? source.type;
  const providerName = name.value || typeName;
  const testFailure =
    testError ??
    (testResult && !testResult.success ? (testResult.error ?? '') : null);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader
        subtitle={id ? (name.previousValue ?? name.value) : undefined}
      >
        {id
          ? translate('EditSourceImplementation', {
              implementationName: typeName,
            })
          : translate('AddSourceImplementation', {
              implementationName: typeName,
            })}
      </ModalHeader>

      <ModalBody>
        <Form
          validationErrors={validationErrors}
          validationWarnings={validationWarnings}
        >
          <FormRow>
            <FormLabel>{translate('Name')}</FormLabel>
            <FormInput
              type={inputTypes.TEXT}
              name="name"
              {...name}
              onChange={handleInputChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('Enabled')}</FormLabel>
            <FormInput
              type={inputTypes.CHECK}
              name="enabled"
              helpText={translate('SourceEnabledHelpText')}
              {...enabled}
              onChange={handleInputChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('RefreshInterval')}</FormLabel>
            <FormInputHelpText text={translate('RefreshIntervalHelpText')} />
            <FormInput
              type={inputTypes.NUMBER}
              name="refreshInterval"
              min={15}
              unit={translate('Minutes')}
              {...refreshInterval}
              onChange={handleInputChange}
            />
          </FormRow>

          {fields?.map((field) => (
            <SourceFieldFormGroup
              key={field.name}
              advancedSettings={showAdvancedSettings}
              {...field}
              onChange={handleFieldChange}
            />
          ))}
        </Form>

        {testResult ? <SourcePreview result={testResult} /> : null}
      </ModalBody>

      <ModalFooter>
        {id ? (
          <Button
            className={styles.deleteButton}
            kind={kinds.DANGER}
            onPress={onDeletePress}
          >
            {translate('Delete')}
          </Button>
        ) : null}

        <AdvancedSettingsButton showLabel={false} />

        <SpinnerErrorButton
          isSpinning={isTesting}
          error={testFailure}
          pendingLabel={translate('ProviderTestInProgress', {
            name: providerName,
          })}
          successLabel={translate('ProviderTestSuccessful', {
            name: providerName,
          })}
          warningLabel={translate('ProviderTestWarning', {
            name: providerName,
          })}
          errorLabel={translate('ProviderTestFailed', { name: providerName })}
          onPress={testSource}
        >
          {translate('Test')}
        </SpinnerErrorButton>

        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerErrorButton
          isSpinning={isSaving}
          error={saveError}
          onPress={saveSource}
        >
          {translate('Save')}
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default EditSourceModalContent;
