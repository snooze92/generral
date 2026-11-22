// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/DownloadClients/DownloadClients/EditDownloadClientModalContent.tsx
import React, { useCallback } from 'react';
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
import DownloadClient, { PATH } from './DownloadClient';

interface EditDownloadClientModalContentProps {
  id?: number;
  implementation?: string;
  onModalClose: () => void;
  onDeletePress?: () => void;
}

function EditDownloadClientModalContent({
  id,
  implementation,
  onModalClose,
  onDeletePress,
}: EditDownloadClientModalContentProps) {
  const selectSchema = useCallback(
    (schema: DownloadClient[]) =>
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
  } = useManageProvider<DownloadClient>({
    path: PATH,
    id,
    selectSchema,
    onSaved: onModalClose,
  });

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      updateValue(name as keyof DownloadClient, value as never);
    },
    [updateValue]
  );

  if (!provider) {
    return null;
  }

  const {
    name,
    enable,
    priority,
    removeCompletedDownloads,
    removeFailedDownloads,
    tags,
    fields,
  } = item;
  const implementationName = provider.implementationName;

  return (
    <EditProviderModalContent
      title={
        id
          ? translate('EditDownloadClientImplementation', {
              implementationName,
            })
          : translate('AddDownloadClientImplementation', {
              implementationName,
            })
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
      after={
        <>
          <FormRow>
            <FormLabel>{translate('ClientPriority')}</FormLabel>
            <FormInputHelpText
              text={translate('DownloadClientPriorityHelpText')}
            />
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
            <FormLabel>{translate('RemoveCompleted')}</FormLabel>
            <FormInput
              type={inputTypes.CHECK}
              name="removeCompletedDownloads"
              helpText={translate('RemoveCompletedDownloadsHelpText')}
              {...removeCompletedDownloads}
              onChange={handleInputChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('RemoveFailed')}</FormLabel>
            <FormInput
              type={inputTypes.CHECK}
              name="removeFailedDownloads"
              helpText={translate('RemoveFailedDownloadsHelpText')}
              {...removeFailedDownloads}
              onChange={handleInputChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('Tags')}</FormLabel>
            <FormInputHelpText
              text={translate('DownloadClientSeriesTagHelpText')}
            />
            <FormInput
              type={inputTypes.TAG}
              name="tags"
              {...tags}
              onChange={handleInputChange}
            />
          </FormRow>
        </>
      }
      onFieldChange={updateFieldValue}
      onTestPress={testProvider}
      onSavePress={saveProvider}
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
        <FormLabel>{translate('Enable')}</FormLabel>
        <FormInput
          type={inputTypes.CHECK}
          name="enable"
          {...enable}
          onChange={handleInputChange}
        />
      </FormRow>
    </EditProviderModalContent>
  );
}

interface EditDownloadClientModalProps {
  isOpen: boolean;
  id?: number;
  onModalClose: () => void;
  onDeletePress?: () => void;
}

const getTypes = (schema: DownloadClient[]) =>
  schema.map((s) => ({ key: s.implementation, name: s.implementationName }));

/** Edits a download client, or without an id, picks an implementation and then adds one. */
function EditDownloadClientModal({
  isOpen,
  id,
  onModalClose,
  onDeletePress,
}: EditDownloadClientModalProps) {
  return (
    <ProviderModal<DownloadClient>
      isOpen={isOpen}
      id={id}
      path={PATH}
      title={translate('AddDownloadClient')}
      errorMessage={translate('AddDownloadClientError')}
      getTypes={getTypes}
      renderEdit={(implementation, onClose) => (
        <EditDownloadClientModalContent
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

export default EditDownloadClientModal;
