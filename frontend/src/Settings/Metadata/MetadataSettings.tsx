// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Metadata/MetadataSettings.tsx,
// Metadata/Metadatas.tsx, Metadata.tsx and EditMetadataModalContent.tsx. Consumers are added from
// the schema like the other providers, with a Plex library setup help box.
import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import FormInput from 'Components/Form/FormInput';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import Label from 'Components/Label';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import PageSectionContent from 'Components/Page/PageSectionContent';
import SectionHeading from 'Components/SectionHeading';
import AddCard from 'Components/SettingsCard/AddCard';
import settingsCardStyles from 'Components/SettingsCard/SettingsCard.module.css';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import EditProviderModalContent from 'Settings/Provider/EditProviderModalContent';
import { Provider } from 'Settings/Provider/Provider';
import ProviderCard from 'Settings/Provider/ProviderCard';
import ProviderModal from 'Settings/Provider/ProviderModal';
import { useManageProvider, useProviders } from 'Settings/Provider/useProviders';
import settingsStyles from 'Settings/Settings.module.css';
import SettingsPage from 'Settings/SettingsPage';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';

interface Metadata extends Provider {
  enable: boolean;
}

const PATH = '/metadata';

interface EditMetadataModalContentProps {
  id?: number;
  implementation?: string;
  onModalClose: () => void;
  onDeletePress?: () => void;
}

function EditMetadataModalContent({
  id,
  implementation,
  onModalClose,
  onDeletePress,
}: EditMetadataModalContentProps) {
  const selectSchema = useCallback(
    (schema: Metadata[]) =>
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
  } = useManageProvider<Metadata>({
    path: PATH,
    id,
    selectSchema,
    onSaved: onModalClose,
  });

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      updateValue(name as keyof Metadata, value as never);
    },
    [updateValue]
  );

  if (!provider) {
    return null;
  }

  const { name, enable, fields } = item;
  const implementationName = provider.implementationName;

  return (
    <EditProviderModalContent
      title={translate('EditMetadata', { metadataType: implementationName })}
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
          helpText={translate('EnableMetadataHelpText')}
          {...enable}
          onChange={handleInputChange}
        />
      </FormRow>
    </EditProviderModalContent>
  );
}

const getTypes = (schema: Metadata[]) =>
  schema.map((s) => ({ key: s.implementation, name: s.implementationName }));

interface EditMetadataModalProps {
  isOpen: boolean;
  id?: number;
  onModalClose: () => void;
  onDeletePress?: () => void;
}

function EditMetadataModal({
  isOpen,
  id,
  onModalClose,
  onDeletePress,
}: EditMetadataModalProps) {
  return (
    <ProviderModal<Metadata>
      isOpen={isOpen}
      id={id}
      path={PATH}
      title={translate('AddMetadata')}
      errorMessage={translate('MetadataLoadError')}
      getTypes={getTypes}
      renderEdit={(implementation, onClose) => (
        <EditMetadataModalContent
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

function MetadataCard({ metadata }: { metadata: Metadata }) {
  const { id, name, enable, fields } = metadata;
  const enabledOptions = fields.filter((f) => f.value === true);

  return (
    <ProviderCard
      path={PATH}
      id={id}
      name={name}
      editLabel={translate('EditMetadata', {
        metadataType: metadata.implementationName,
      })}
      deleteTitle={translate('DeleteMetadata')}
      deleteMessage={translate('DeleteMetadataMessageText', { name })}
      labels={
        enable ? (
          enabledOptions.map((field) => (
            <Label key={field.name} kind={kinds.SUCCESS} size={sizes.MEDIUM}>
              {field.label}
            </Label>
          ))
        ) : (
          <Label kind={kinds.DISABLED} outline={true} size={sizes.MEDIUM}>
            {translate('Disabled')}
          </Label>
        )
      }
      renderModal={(modalProps) => <EditMetadataModal id={id} {...modalProps} />}
    />
  );
}

function MetadataSettings() {
  const { data, isFetching, isFetched, error } = useProviders<Metadata>(PATH);
  const [isAddOpen, setIsAddOpen] = useState(false);

  return (
    <SettingsPage title={translate('MetadataSettings')} showSave={false}>
      <PageContentBody>
        <div className={settingsStyles.section}>
          <PageHeading
            scope={translate('Settings')}
            title={translate('Metadata')}
          />

          <div className={settingsStyles.pageSection}>
            <SectionHeading title={translate('Metadata')} />

            <Alert kind={kinds.INFO}>
              <div>{translate('PlexNfoLibraryHelpText')}</div>
              <div>{translate('PlexPersonalMediaHelpText')}</div>
            </Alert>

            <PageSectionContent
              errorMessage={translate('MetadataLoadError')}
              error={error}
              isFetching={isFetching}
              isPopulated={isFetched}
            >
              <div className={settingsCardStyles.grid}>
                {data.map((metadata) => (
                  <MetadataCard key={metadata.id} metadata={metadata} />
                ))}

                <AddCard
                  label={translate('AddMetadata')}
                  onPress={() => setIsAddOpen(true)}
                />
              </div>

              <EditMetadataModal
                isOpen={isAddOpen}
                onModalClose={() => setIsAddOpen(false)}
              />
            </PageSectionContent>
          </div>
        </div>
      </PageContentBody>
    </SettingsPage>
  );
}

export default MetadataSettings;
