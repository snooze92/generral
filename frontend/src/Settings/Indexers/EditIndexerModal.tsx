// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Indexers/Indexers/EditIndexerModalContent.tsx
import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { inputTypes } from 'Helpers/Props';
import EditProviderModalContent from 'Settings/Provider/EditProviderModalContent';
import ProviderModal from 'Settings/Provider/ProviderModal';
import { useManageProvider } from 'Settings/Provider/useProviders';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import Indexer, { PATH } from './Indexer';

interface EditIndexerModalContentProps {
  id?: number;
  implementation?: string;
  onModalClose: () => void;
  onDeletePress?: () => void;
}

function EditIndexerModalContent({
  id,
  implementation,
  onModalClose,
  onDeletePress,
}: EditIndexerModalContentProps) {
  const selectSchema = useCallback(
    (schema: Indexer[]) =>
      schema.find((s) => s.implementation === implementation),
    [implementation]
  );

  const {
    provider,
    item,
    updateValue,
    updateFieldValue,
    saveProvider,
    isSaving,
    saveError,
    testProvider,
    isTesting,
    testError,
    validationErrors,
    validationWarnings,
  } = useManageProvider<Indexer>({
    path: PATH,
    id,
    selectSchema,
    onSaved: onModalClose,
  });

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      updateValue(name as keyof Indexer, value as never);
    },
    [updateValue]
  );

  if (!provider) {
    return null;
  }

  const {
    name,
    enableRss,
    enableAutomaticSearch,
    enableInteractiveSearch,
    priority,
    tags,
    fields,
  } = item;
  const implementationName = provider.implementationName;

  return (
    <EditProviderModalContent
      title={
        id
          ? translate('EditIndexerImplementation', { implementationName })
          : translate('AddIndexerImplementation', { implementationName })
      }
      subtitle={id ? (name.previousValue ?? name.value) : undefined}
      providerName={name.value || implementationName}
      fields={fields}
      validationErrors={validationErrors}
      validationWarnings={validationWarnings}
      isTesting={isTesting}
      testFailure={testError}
      isSaving={isSaving}
      saveError={saveError}
      onFieldChange={updateFieldValue}
      onTestPress={testProvider}
      onSavePress={saveProvider}
      onDeletePress={id ? onDeletePress : undefined}
      onModalClose={onModalClose}
    >
      {provider.message ? (
        <Alert kind={provider.message.type}>{provider.message.message}</Alert>
      ) : null}

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
        <FormLabel>{translate('EnableRss')}</FormLabel>
        <FormInput
          type={inputTypes.CHECK}
          name="enableRss"
          helpText={
            provider.supportsRss
              ? translate('EnableRssHelpText')
              : translate('RssIsNotSupportedWithThisIndexer')
          }
          isDisabled={!provider.supportsRss}
          {...enableRss}
          onChange={handleInputChange}
        />
      </FormRow>

      <FormRow>
        <FormLabel>{translate('EnableAutomaticSearch')}</FormLabel>
        <FormInput
          type={inputTypes.CHECK}
          name="enableAutomaticSearch"
          helpText={
            provider.supportsSearch
              ? translate('EnableAutomaticSearchHelpText')
              : translate('SearchIsNotSupportedWithThisIndexer')
          }
          isDisabled={!provider.supportsSearch}
          {...enableAutomaticSearch}
          onChange={handleInputChange}
        />
      </FormRow>

      <FormRow>
        <FormLabel>{translate('EnableInteractiveSearch')}</FormLabel>
        <FormInput
          type={inputTypes.CHECK}
          name="enableInteractiveSearch"
          helpText={
            provider.supportsSearch
              ? translate('EnableInteractiveSearchHelpText')
              : translate('SearchIsNotSupportedWithThisIndexer')
          }
          isDisabled={!provider.supportsSearch}
          {...enableInteractiveSearch}
          onChange={handleInputChange}
        />
      </FormRow>

      <FormRow>
        <FormLabel>{translate('IndexerPriority')}</FormLabel>
        <FormInputHelpText text={translate('IndexerPriorityHelpText')} />
        <FormInput
          type={inputTypes.NUMBER}
          name="priority"
          min={1}
          max={50}
          {...priority}
          onChange={handleInputChange}
        />
      </FormRow>

      <FormRow>
        <FormLabel>{translate('Tags')}</FormLabel>
        <FormInputHelpText text={translate('IndexerTagSeriesHelpText')} />
        <FormInput
          type={inputTypes.TAG}
          name="tags"
          {...tags}
          onChange={handleInputChange}
        />
      </FormRow>
    </EditProviderModalContent>
  );
}

interface EditIndexerModalProps {
  isOpen: boolean;
  id?: number;
  onModalClose: () => void;
  onDeletePress?: () => void;
}

const getTypes = (schema: Indexer[]) =>
  schema.map((s) => ({ key: s.implementation, name: s.implementationName }));

/** Edits an indexer, or without an id, picks an implementation and then adds one. */
function EditIndexerModal({
  isOpen,
  id,
  onModalClose,
  onDeletePress,
}: EditIndexerModalProps) {
  return (
    <ProviderModal<Indexer>
      isOpen={isOpen}
      id={id}
      path={PATH}
      title={translate('AddIndexer')}
      errorMessage={translate('AddIndexerError')}
      getTypes={getTypes}
      renderEdit={(implementation, onClose) => (
        <EditIndexerModalContent
          id={id}
          implementation={implementation}
          onModalClose={onClose}
          onDeletePress={onDeletePress}
        />
      )}
      onModalClose={onModalClose}
    />
  );
}

export default EditIndexerModal;
