// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Tags/TagSettings.tsx
// Without the Auto Tagging section (Later).
import React from 'react';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import SectionHeading from 'Components/SectionHeading';
import settingsStyles from 'Settings/Settings.module.css';
import SettingsPage from 'Settings/SettingsPage';
import translate from 'Utilities/String/translate';
import Tags from './Tags';

function TagSettings() {
  return (
    <SettingsPage title={translate('Tags')} showSave={false}>
      <PageContentBody>
        <div className={settingsStyles.section}>
          <PageHeading
            scope={translate('Settings')}
            title={translate('Tags')}
          />

          <div className={settingsStyles.pageSection}>
            <SectionHeading
              title={translate('Tags')}
              description={translate('TagsSectionDescription')}
            />

            <Tags />
          </div>
        </div>
      </PageContentBody>
    </SettingsPage>
  );
}

export default TagSettings;
