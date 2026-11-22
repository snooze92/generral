// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Indexers/IndexerSettings.tsx
// Without Test All / Manage Indexers (Later); with a Prowlarr help box (docs/specs/10-ui.md).
import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
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
import ProviderCard from 'Settings/Provider/ProviderCard';
import { useProviders } from 'Settings/Provider/useProviders';
import settingsStyles from 'Settings/Settings.module.css';
import SettingsPage from 'Settings/SettingsPage';
import { useManageSettings } from 'Settings/useSettings';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import EditIndexerModal from './EditIndexerModal';
import Indexer, { IndexerOptions, OPTIONS_PATH, PATH } from './Indexer';

function IndexerCard({ indexer }: { indexer: Indexer }) {
  const { id, name, enableRss, enableAutomaticSearch, enableInteractiveSearch } =
    indexer;

  return (
    <ProviderCard
      path={PATH}
      id={id}
      name={name}
      editLabel={translate('EditIndexerImplementation', {
        implementationName: indexer.implementationName,
      })}
      deleteTitle={translate('DeleteIndexer')}
      deleteMessage={translate('DeleteIndexerMessageText', { name })}
      tags={indexer.tags}
      labels={
        <>
          <Label kind={kinds.INFO} size={sizes.MEDIUM}>
            {indexer.implementationName}
          </Label>

          {indexer.supportsRss && enableRss ? (
            <Label size={sizes.MEDIUM}>{translate('Rss')}</Label>
          ) : null}

          {indexer.supportsSearch && enableAutomaticSearch ? (
            <Label size={sizes.MEDIUM}>{translate('AutomaticSearch')}</Label>
          ) : null}

          {indexer.supportsSearch && enableInteractiveSearch ? (
            <Label size={sizes.MEDIUM}>{translate('InteractiveSearch')}</Label>
          ) : null}

          {!enableRss && !enableAutomaticSearch && !enableInteractiveSearch ? (
            <Label kind={kinds.DISABLED} outline={true} size={sizes.MEDIUM}>
              {translate('Disabled')}
            </Label>
          ) : null}
        </>
      }
      renderModal={(modalProps) => <EditIndexerModal id={id} {...modalProps} />}
    >
      {indexer.priority !== 25 ? (
        <div className={settingsCardStyles.statusLine}>
          {translate('Priority')} {indexer.priority}
        </div>
      ) : null}
    </ProviderCard>
  );
}

function Indexers() {
  const { data, isFetching, isFetched, error } = useProviders<Indexer>(PATH);
  const [isAddOpen, setIsAddOpen] = useState(false);

  return (
    <PageSectionContent
      errorMessage={translate('IndexersLoadError')}
      error={error}
      isFetching={isFetching}
      isPopulated={isFetched}
    >
      <div className={settingsCardStyles.grid}>
        {data.map((indexer) => (
          <IndexerCard key={indexer.id} indexer={indexer} />
        ))}

        <AddCard
          label={translate('AddIndexer')}
          onPress={() => setIsAddOpen(true)}
        />
      </div>

      <EditIndexerModal
        isOpen={isAddOpen}
        onModalClose={() => setIsAddOpen(false)}
      />
    </PageSectionContent>
  );
}

function IndexerSettings() {
  const {
    settings,
    isFetching,
    isFetched,
    error,
    hasPendingChanges,
    hasSettings,
    isSaving,
    validationErrors,
    validationWarnings,
    saveSettings,
    updateSetting,
  } = useManageSettings<IndexerOptions>(OPTIONS_PATH);

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      updateSetting(name as keyof IndexerOptions, value as never);
    },
    [updateSetting]
  );

  return (
    <SettingsPage
      title={translate('IndexerSettings')}
      hasPendingChanges={hasPendingChanges}
      isSaving={isSaving}
      onSavePress={saveSettings}
    >
      <PageContentBody>
        <div className={settingsStyles.section}>
          <PageHeading
            scope={translate('Settings')}
            title={translate('Indexers')}
          />

          <div className={settingsStyles.pageSection}>
            <SectionHeading
              title={translate('Indexers')}
              description={translate('IndexersSectionDescription')}
            />

            <Alert kind={kinds.INFO}>{translate('ProwlarrSyncHelpText')}</Alert>

            <Indexers />
          </div>

          <PageSectionContent
            errorMessage={translate('IndexerOptionsLoadError')}
            error={error}
            isFetching={isFetching}
            isPopulated={isFetched && hasSettings}
          >
            <Form
              validationErrors={validationErrors}
              validationWarnings={validationWarnings}
            >
              <FieldSet legend={translate('Options')}>
                <FormRow size={sizes.MEDIUM}>
                  <FormLabel>{translate('RssSyncInterval')}</FormLabel>
                  <FormInputHelpText
                    text={translate('RssSyncIntervalHelpText')}
                  />
                  <FormInputHelpText
                    text={translate('RssSyncIntervalHelpTextWarning')}
                    isWarning={true}
                  />
                  <FormInput
                    type={inputTypes.NUMBER}
                    name="rssSyncInterval"
                    min={0}
                    max={120}
                    unit={translate('Minutes')}
                    {...settings.rssSyncInterval}
                    onChange={handleInputChange}
                  />
                </FormRow>
              </FieldSet>
            </Form>
          </PageSectionContent>
        </div>
      </PageContentBody>
    </SettingsPage>
  );
}

export default IndexerSettings;
