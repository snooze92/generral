// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Indexers/Indexers/EditIndexerModalContent.tsx
import React, { useCallback } from 'react';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { inputTypes } from 'Helpers/Props';
import EditProviderModalContent from 'Settings/Provider/EditProviderModalContent';
import Source from 'Sources/Source';
import { useManageSource } from 'Sources/useSources';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import SourcePreview from './SourcePreview';

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

  if (!source) {
    return null;
  }

  const { name, enabled, refreshInterval, fields } = item;
  const typeName = source.typeName ?? source.type;
  const testFailure =
    testError ??
    (testResult && !testResult.success ? (testResult.error ?? '') : null);

  return (
    <EditProviderModalContent
      title={
        id
          ? translate('EditSourceImplementation', {
              implementationName: typeName,
            })
          : translate('AddSourceImplementation', {
              implementationName: typeName,
            })
      }
      subtitle={id ? (name.previousValue ?? name.value) : undefined}
      providerName={name.value || typeName}
      fields={fields}
      validationErrors={validationErrors}
      validationWarnings={validationWarnings}
      isTesting={isTesting}
      testFailure={testFailure}
      isSaving={isSaving}
      saveError={saveError}
      after={testResult ? <SourcePreview result={testResult} /> : null}
      onFieldChange={updateFieldValue}
      onTestPress={testSource}
      onSavePress={saveSource}
      onDeletePress={id ? onDeletePress : undefined}
      onModalClose={onModalClose}
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
    </EditProviderModalContent>
  );
}

export default EditSourceModalContent;
