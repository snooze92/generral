// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/MediaManagement/MediaManagement.tsx
// Only the options Generral implements (docs/specs/08-import-and-organisation.md); remote path
// mappings are listed here rather than on Download Clients.
import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import Link from 'Components/Link/Link';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import PageSectionContent from 'Components/Page/PageSectionContent';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import { useShowAdvancedSettings } from 'Settings/advancedSettingsStore';
import settingsStyles from 'Settings/Settings.module.css';
import SettingsPage from 'Settings/SettingsPage';
import { useManageSettings } from 'Settings/useSettings';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import {
  MEDIA_MANAGEMENT_PATH,
  MediaManagementConfig,
  NAMING_PATH,
  NamingConfig,
} from './MediaManagement';
import Naming from './Naming';
import RemotePathMappings from './RemotePathMappings';
import RootFolders from './RootFolders';

function MediaManagementSettings() {
  const showAdvancedSettings = useShowAdvancedSettings();
  const naming = useManageSettings<NamingConfig>(NAMING_PATH);
  const media = useManageSettings<MediaManagementConfig>(MEDIA_MANAGEMENT_PATH);
  const { settings } = media;

  const { updateSetting: updateNaming } = naming;
  const { updateSetting: updateMedia } = media;

  const handleNamingChange = useCallback(
    ({ name, value }: InputChanged) =>
      updateNaming(name as keyof NamingConfig, value as never),
    [updateNaming]
  );

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) =>
      updateMedia(name as keyof MediaManagementConfig, value as never),
    [updateMedia]
  );

  const handleSavePress = useCallback(() => {
    if (naming.hasPendingChanges) {
      naming.saveSettings();
    }

    if (media.hasPendingChanges) {
      media.saveSettings();
    }
  }, [naming, media]);

  return (
    <SettingsPage
      title={translate('MediaManagementSettings')}
      hasPendingChanges={naming.hasPendingChanges || media.hasPendingChanges}
      isSaving={naming.isSaving || media.isSaving}
      onSavePress={handleSavePress}
    >
      <PageContentBody>
        <div className={settingsStyles.section}>
          <PageHeading
            scope={translate('Settings')}
            title={translate('MediaManagement')}
          />

          <PageSectionContent
            errorMessage={translate('NamingSettingsLoadError')}
            error={naming.error}
            isFetching={naming.isFetching}
            isPopulated={naming.isFetched && naming.hasSettings}
          >
            <Form
              validationErrors={naming.validationErrors}
              validationWarnings={naming.validationWarnings}
            >
              <Naming
                settings={naming.settings}
                onInputChange={handleNamingChange}
              />
            </Form>
          </PageSectionContent>

          <Alert kind={kinds.INFO}>
            {translate('MetadataApproachHint')}{' '}
            <Link to="/settings/metadata">{translate('Metadata')}</Link>
          </Alert>

          <PageSectionContent
            errorMessage={translate('MediaManagementSettingsLoadError')}
            error={media.error}
            isFetching={media.isFetching}
            isPopulated={media.isFetched && media.hasSettings}
          >
            <Form
              validationErrors={media.validationErrors}
              validationWarnings={media.validationWarnings}
            >
              <FieldSet legend={translate('Importing')}>
                {showAdvancedSettings ? (
                  <FormRow size={sizes.MEDIUM}>
                    <FormLabel>
                      {translate('SkipFreeSpaceCheckWhenImporting')}
                    </FormLabel>
                    <FormInputHelpText
                      text={translate('SkipFreeSpaceCheckWhenImportingHelpText')}
                    />
                    <FormInput
                      type={inputTypes.CHECK}
                      name="skipFreeSpaceCheckWhenImporting"
                      {...settings.skipFreeSpaceCheckWhenImporting}
                      onChange={handleInputChange}
                    />
                  </FormRow>
                ) : null}

                {showAdvancedSettings ? (
                  <FormRow size={sizes.MEDIUM}>
                    <FormLabel>{translate('MinimumFreeSpace')}</FormLabel>
                    <FormInputHelpText
                      text={translate('MinimumFreeSpaceHelpText')}
                    />
                    <FormInput
                      type={inputTypes.NUMBER}
                      name="minimumFreeSpaceWhenImporting"
                      min={0}
                      unit="MB"
                      {...settings.minimumFreeSpaceWhenImporting}
                      onChange={handleInputChange}
                    />
                  </FormRow>
                ) : null}

                <FormRow size={sizes.MEDIUM}>
                  <FormLabel>{translate('UseHardlinksInsteadOfCopy')}</FormLabel>
                  <FormInputHelpText
                    text={translate('CopyUsingHardlinksSeriesHelpText')}
                  />
                  <FormInput
                    type={inputTypes.CHECK}
                    name="copyUsingHardlinks"
                    {...settings.copyUsingHardlinks}
                    onChange={handleInputChange}
                  />
                </FormRow>

                <FormRow size={sizes.MEDIUM}>
                  <FormLabel>{translate('ImportExtraFiles')}</FormLabel>
                  <FormInputHelpText
                    text={translate('ImportExtraFilesEpisodeHelpText')}
                  />
                  <FormInput
                    type={inputTypes.CHECK}
                    name="importExtraFiles"
                    {...settings.importExtraFiles}
                    onChange={handleInputChange}
                  />
                </FormRow>

                {settings.importExtraFiles?.value ? (
                  <FormRow size={sizes.MEDIUM}>
                    <FormLabel>{translate('ExtraFileExtensions')}</FormLabel>
                    <FormInputHelpText
                      text={translate('ExtraFileExtensionsHelpText')}
                    />
                    <FormInput
                      type={inputTypes.TEXT}
                      name="extraFileExtensions"
                      {...settings.extraFileExtensions}
                      onChange={handleInputChange}
                    />
                  </FormRow>
                ) : null}
              </FieldSet>

              <FieldSet legend={translate('FileManagement')}>
                <FormRow size={sizes.MEDIUM}>
                  <FormLabel>{translate('DeleteEmptyFolders')}</FormLabel>
                  <FormInputHelpText
                    text={translate('DeleteEmptySeriesFoldersHelpText')}
                  />
                  <FormInput
                    type={inputTypes.CHECK}
                    name="deleteEmptyFolders"
                    {...settings.deleteEmptyFolders}
                    onChange={handleInputChange}
                  />
                </FormRow>

                <FormRow size={sizes.MEDIUM}>
                  <FormLabel>{translate('RecyclingBin')}</FormLabel>
                  <FormInputHelpText text={translate('RecyclingBinHelpText')} />
                  <FormInput
                    type={inputTypes.TEXT}
                    name="recycleBin"
                    {...settings.recycleBin}
                    onChange={handleInputChange}
                  />
                </FormRow>

                <FormRow size={sizes.MEDIUM}>
                  <FormLabel>{translate('RecyclingBinCleanup')}</FormLabel>
                  <FormInputHelpText
                    text={translate('RecyclingBinCleanupHelpText')}
                  />
                  <FormInput
                    type={inputTypes.NUMBER}
                    name="recycleBinCleanupDays"
                    min={0}
                    max={365}
                    unit={translate('Days')}
                    {...settings.recycleBinCleanupDays}
                    onChange={handleInputChange}
                  />
                </FormRow>
              </FieldSet>

              {showAdvancedSettings ? (
                <FieldSet legend={translate('Permissions')}>
                  <FormRow size={sizes.MEDIUM}>
                    <FormLabel>{translate('SetPermissions')}</FormLabel>
                    <FormInputHelpText
                      text={translate('SetPermissionsLinuxHelpText')}
                    />
                    <FormInput
                      type={inputTypes.CHECK}
                      name="setPermissionsLinux"
                      {...settings.setPermissionsLinux}
                      onChange={handleInputChange}
                    />
                  </FormRow>

                  <FormRow size={sizes.MEDIUM}>
                    <FormLabel>{translate('ChmodFolder')}</FormLabel>
                    <FormInputHelpText text={translate('ChmodFolderHelpText')} />
                    <FormInput
                      type={inputTypes.TEXT}
                      name="chmodFolder"
                      readOnly={!settings.setPermissionsLinux?.value}
                      {...settings.chmodFolder}
                      onChange={handleInputChange}
                    />
                  </FormRow>

                  <FormRow size={sizes.MEDIUM}>
                    <FormLabel>{translate('ChownGroup')}</FormLabel>
                    <FormInputHelpText text={translate('ChownGroupHelpText')} />
                    <FormInput
                      type={inputTypes.TEXT}
                      name="chownGroup"
                      readOnly={!settings.setPermissionsLinux?.value}
                      {...settings.chownGroup}
                      onChange={handleInputChange}
                    />
                  </FormRow>
                </FieldSet>
              ) : null}
            </Form>
          </PageSectionContent>

          <FieldSet legend={translate('RootFolders')}>
            <RootFolders />
          </FieldSet>

          <FieldSet legend={translate('RemotePathMappings')}>
            <FormInputHelpText text={translate('RemotePathMappingsInfo')} />
            <RemotePathMappings />
          </FieldSet>
        </div>
      </PageContentBody>
    </SettingsPage>
  );
}

export default MediaManagementSettings;
