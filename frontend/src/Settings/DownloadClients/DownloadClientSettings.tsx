// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/DownloadClients/DownloadClientSettings.tsx
// Without Test All / Manage Clients (Later); remote path mappings are on Media Management. Completed download handling
// is always on (M3 imports), so the options are the failed download handling ones.
import React, { useCallback, useState } from 'react';
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
import DownloadClient, {
  DownloadClientOptions,
  OPTIONS_PATH,
  PATH,
} from './DownloadClient';
import EditDownloadClientModal from './EditDownloadClientModal';

function DownloadClientCard({ client }: { client: DownloadClient }) {
  const { id, name, enable, priority } = client;

  return (
    <ProviderCard
      path={PATH}
      id={id}
      name={name}
      editLabel={translate('EditDownloadClientImplementation', {
        implementationName: client.implementationName,
      })}
      deleteTitle={translate('DeleteDownloadClient')}
      deleteMessage={translate('DeleteDownloadClientMessageText', { name })}
      tags={client.tags}
      labels={
        <>
          <Label kind={kinds.INFO} size={sizes.MEDIUM}>
            {client.implementationName}
          </Label>

          {enable ? (
            <Label kind={kinds.SUCCESS} size={sizes.MEDIUM}>
              {translate('Enabled')}
            </Label>
          ) : (
            <Label kind={kinds.DISABLED} outline={true} size={sizes.MEDIUM}>
              {translate('Disabled')}
            </Label>
          )}
        </>
      }
      renderModal={(modalProps) => (
        <EditDownloadClientModal id={id} {...modalProps} />
      )}
    >
      {priority > 1 ? (
        <div className={settingsCardStyles.statusLine}>
          {translate('Priority')} {priority}
        </div>
      ) : null}
    </ProviderCard>
  );
}

function DownloadClients() {
  const { data, isFetching, isFetched, error } =
    useProviders<DownloadClient>(PATH);
  const [isAddOpen, setIsAddOpen] = useState(false);

  return (
    <PageSectionContent
      errorMessage={translate('DownloadClientsLoadError')}
      error={error}
      isFetching={isFetching}
      isPopulated={isFetched}
    >
      <div className={settingsCardStyles.grid}>
        {data.map((client) => (
          <DownloadClientCard key={client.id} client={client} />
        ))}

        <AddCard
          label={translate('AddDownloadClient')}
          onPress={() => setIsAddOpen(true)}
        />
      </div>

      <EditDownloadClientModal
        isOpen={isAddOpen}
        onModalClose={() => setIsAddOpen(false)}
      />
    </PageSectionContent>
  );
}

function DownloadClientSettings() {
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
  } = useManageSettings<DownloadClientOptions>(OPTIONS_PATH);

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      updateSetting(name as keyof DownloadClientOptions, value as never);
    },
    [updateSetting]
  );

  return (
    <SettingsPage
      title={translate('DownloadClientSettings')}
      hasPendingChanges={hasPendingChanges}
      isSaving={isSaving}
      onSavePress={saveSettings}
    >
      <PageContentBody>
        <div className={settingsStyles.section}>
          <PageHeading
            scope={translate('Settings')}
            title={translate('DownloadClients')}
          />

          <div className={settingsStyles.pageSection}>
            <SectionHeading
              title={translate('DownloadClients')}
              description={translate('DownloadClientsSectionDescription')}
            />

            <DownloadClients />
          </div>

          <PageSectionContent
            errorMessage={translate('DownloadClientOptionsLoadError')}
            error={error}
            isFetching={isFetching}
            isPopulated={isFetched && hasSettings}
          >
            <Form
              validationErrors={validationErrors}
              validationWarnings={validationWarnings}
            >
              <FieldSet legend={translate('FailedDownloadHandling')}>
                <FormRow size={sizes.MEDIUM}>
                  <FormLabel>{translate('AutoRedownloadFailed')}</FormLabel>
                  <FormInputHelpText
                    text={translate('AutoRedownloadFailedHelpText')}
                  />
                  <FormInput
                    type={inputTypes.CHECK}
                    name="autoRedownloadFailed"
                    {...settings.autoRedownloadFailed}
                    onChange={handleInputChange}
                  />
                </FormRow>

                {settings.autoRedownloadFailed?.value ? (
                  <FormRow size={sizes.MEDIUM}>
                    <FormLabel>
                      {translate('AutoRedownloadFailedFromInteractiveSearch')}
                    </FormLabel>
                    <FormInputHelpText
                      text={translate(
                        'AutoRedownloadFailedFromInteractiveSearchHelpText'
                      )}
                    />
                    <FormInput
                      type={inputTypes.CHECK}
                      name="autoRedownloadFailedFromInteractiveSearch"
                      {...settings.autoRedownloadFailedFromInteractiveSearch}
                      onChange={handleInputChange}
                    />
                  </FormRow>
                ) : null}

                <FormRow size={sizes.MEDIUM}>
                  <FormLabel>{translate('StalledDownloadTimeout')}</FormLabel>
                  <FormInputHelpText
                    text={translate('StalledDownloadTimeoutHelpText')}
                  />
                  <FormInput
                    type={inputTypes.NUMBER}
                    name="stalledDownloadTimeout"
                    min={0}
                    max={168}
                    unit={translate('Hours')}
                    {...settings.stalledDownloadTimeout}
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

export default DownloadClientSettings;
