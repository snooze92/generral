// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Profiles/Profiles.tsx
// Quality and release profiles only (custom formats are Later).
import React, { useState } from 'react';
import Label from 'Components/Label';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import PageSectionContent from 'Components/Page/PageSectionContent';
import SectionHeading from 'Components/SectionHeading';
import AddCard from 'Components/SettingsCard/AddCard';
import settingsCardStyles from 'Components/SettingsCard/SettingsCard.module.css';
import { kinds } from 'Helpers/Props';
import Indexer, { PATH as INDEXER_PATH } from 'Settings/Indexers/Indexer';
import ProviderCard from 'Settings/Provider/ProviderCard';
import { useProviders } from 'Settings/Provider/useProviders';
import settingsStyles from 'Settings/Settings.module.css';
import SettingsPage from 'Settings/SettingsPage';
import translate from 'Utilities/String/translate';
import EditQualityProfileModal from './Quality/EditQualityProfileModal';
import { itemId, itemName } from './Quality/QualityProfileItems';
import useQualityProfiles, {
  PATH as QUALITY_PROFILE_PATH,
} from './Quality/useQualityProfiles';
import EditReleaseProfileModal from './Release/EditReleaseProfileModal';
import useReleaseProfiles, {
  PATH as RELEASE_PROFILE_PATH,
} from './Release/useReleaseProfiles';

function QualityProfiles() {
  const { data, isFetching, isFetched, error } = useQualityProfiles();
  const [isAddOpen, setIsAddOpen] = useState(false);

  return (
    <PageSectionContent
      errorMessage={translate('QualityProfilesLoadError')}
      error={error}
      isFetching={isFetching}
      isPopulated={isFetched}
    >
      <div className={settingsCardStyles.grid}>
        {data.map((profile) => (
          <ProviderCard
            key={profile.id}
            path={QUALITY_PROFILE_PATH}
            id={profile.id}
            name={profile.name}
            editLabel={translate('EditQualityProfile')}
            deleteTitle={translate('DeleteQualityProfile')}
            deleteMessage={translate('DeleteQualityProfileMessageText', {
              name: profile.name,
            })}
            labels={[...profile.items]
              .reverse()
              .filter((item) => item.allowed)
              .map((item) => (
                <Label
                  key={itemId(item)}
                  kind={
                    profile.upgradeAllowed && itemId(item) === profile.cutoff
                      ? kinds.INFO
                      : kinds.DEFAULT
                  }
                  title={
                    profile.upgradeAllowed && itemId(item) === profile.cutoff
                      ? translate('UpgradeUntil')
                      : undefined
                  }
                >
                  {itemName(item)}
                </Label>
              ))}
            renderModal={(modalProps) => (
              <EditQualityProfileModal id={profile.id} {...modalProps} />
            )}
          />
        ))}

        <AddCard
          label={translate('AddQualityProfile')}
          onPress={() => setIsAddOpen(true)}
        />
      </div>

      <EditQualityProfileModal
        isOpen={isAddOpen}
        onModalClose={() => setIsAddOpen(false)}
      />
    </PageSectionContent>
  );
}

function ReleaseProfiles() {
  const { data, isFetching, isFetched, error } = useReleaseProfiles();
  const { data: indexers } = useProviders<Indexer>(INDEXER_PATH);
  const [isAddOpen, setIsAddOpen] = useState(false);

  return (
    <PageSectionContent
      errorMessage={translate('ReleaseProfilesLoadError')}
      error={error}
      isFetching={isFetching}
      isPopulated={isFetched}
    >
      <div className={settingsCardStyles.grid}>
        {data.map((profile) => {
          const name = profile.name || translate('Unnamed');
          const indexer = indexers.find((i) => i.id === profile.indexerId);

          return (
            <ProviderCard
              key={profile.id}
              path={RELEASE_PROFILE_PATH}
              id={profile.id}
              name={name}
              editLabel={translate('EditReleaseProfile')}
              deleteTitle={translate('DeleteReleaseProfile')}
              deleteMessage={translate('DeleteReleaseProfileMessageText', {
                name,
              })}
              tags={profile.tags}
              labels={
                <>
                  {profile.enabled ? null : (
                    <Label kind={kinds.DISABLED} outline={true}>
                      {translate('Disabled')}
                    </Label>
                  )}

                  {profile.required.map((term) => (
                    <Label key={`+${term}`} kind={kinds.SUCCESS}>
                      {term}
                    </Label>
                  ))}

                  {profile.ignored.map((term) => (
                    <Label key={`-${term}`} kind={kinds.DANGER}>
                      {term}
                    </Label>
                  ))}

                  {indexer ? (
                    <Label kind={kinds.INFO} outline={true}>
                      {indexer.name}
                    </Label>
                  ) : null}
                </>
              }
              renderModal={(modalProps) => (
                <EditReleaseProfileModal id={profile.id} {...modalProps} />
              )}
            />
          );
        })}

        <AddCard
          label={translate('AddReleaseProfile')}
          onPress={() => setIsAddOpen(true)}
        />
      </div>

      <EditReleaseProfileModal
        isOpen={isAddOpen}
        onModalClose={() => setIsAddOpen(false)}
      />
    </PageSectionContent>
  );
}

function Profiles() {
  return (
    <SettingsPage title={translate('Profiles')} showSave={false}>
      <PageContentBody>
        <div className={settingsStyles.section}>
          <PageHeading
            scope={translate('Settings')}
            title={translate('Profiles')}
          />

          <div className={settingsStyles.pageSection}>
            <SectionHeading
              title={translate('QualityProfiles')}
              description={translate('QualityProfilesSectionDescription')}
            />

            <QualityProfiles />
          </div>

          <div className={settingsStyles.pageSection}>
            <SectionHeading
              title={translate('ReleaseProfiles')}
              description={translate('ReleaseProfilesSectionDescription')}
            />

            <ReleaseProfiles />
          </div>
        </div>
      </PageContentBody>
    </SettingsPage>
  );
}

export default Profiles;
