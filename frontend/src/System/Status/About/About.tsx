// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/System/Status/About/About.tsx
import React, { useEffect } from 'react';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import FieldSet from 'Components/FieldSet';
import titleCase from 'Utilities/String/titleCase';
import translate from 'Utilities/String/translate';
import useSystemStatus from '../useSystemStatus';
import StartTime from './StartTime';
import styles from './About.module.css';

function About() {
  const { data, refetch } = useSystemStatus();

  const {
    version,
    commit,
    isContainerized,
    runtimeVersion,
    databaseVersion,
    databaseType,
    migrationVersion,
    appData,
    startupPath,
    mode,
    startTime,
  } = data as typeof data & { commit?: string };

  useEffect(() => {
    refetch();
  }, [refetch]);

  return (
    <FieldSet legend={translate('About')}>
      <DescriptionList className={styles.descriptionList}>
        <DescriptionListItem title={translate('Version')} data={version} />

        {commit ? (
          <DescriptionListItem title={translate('Commit')} data={commit} />
        ) : null}

        <DescriptionListItem
          title={translate('DotNetVersion')}
          data={runtimeVersion}
        />

        {isContainerized ? (
          <DescriptionListItem
            title={translate('Docker')}
            data={translate('Yes')}
          />
        ) : null}

        <DescriptionListItem
          title={translate('Database')}
          data={`${titleCase(databaseType)} ${databaseVersion}`}
        />

        <DescriptionListItem
          title={translate('DatabaseMigration')}
          data={migrationVersion}
        />

        <DescriptionListItem
          title={translate('AppDataDirectory')}
          data={appData}
        />

        <DescriptionListItem
          title={translate('StartupDirectory')}
          data={startupPath}
        />

        <DescriptionListItem title={translate('Mode')} data={titleCase(mode)} />

        <DescriptionListItem
          title={translate('Uptime')}
          data={<StartTime startTime={startTime} />}
        />
      </DescriptionList>
    </FieldSet>
  );
}

export default About;
