// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/System/Status/Health/Health.tsx
import React from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Icon, { IconKind } from 'Components/Icon';
import IconButton from 'Components/Link/IconButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import InlineMarkdown from 'Components/Markdown/InlineMarkdown';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { icons, kinds } from 'Helpers/Props';
import titleCase from 'Utilities/String/titleCase';
import translate from 'Utilities/String/translate';
import { useHealth } from '../../useSystemData';
import styles from './Health.module.css';

const columns: Column[] = [
  { className: styles.status, name: 'type', label: '', isVisible: true },
  { name: 'message', label: () => translate('Message'), isVisible: true },
  { name: 'actions', label: () => translate('Actions'), isVisible: true },
];

// Where to fix each check (Sonarr's HealthItemLink).
const SETTINGS_LINKS: Record<string, string> = {
  IndexerRssCheck: '/settings/indexers',
  IndexerSearchCheck: '/settings/indexers',
  IndexerStatusCheck: '/settings/indexers',
  DownloadClientCheck: '/settings/downloadclients',
  DownloadClientStatusCheck: '/settings/downloadclients',
  RootFolderCheck: '/settings/mediamanagement',
  RemotePathMappingCheck: '/settings/downloadclients',
  SourceRefreshCheck: '/sources',
  MappingCheck: '/series',
};

const KINDS: Record<string, IconKind> = {
  error: kinds.DANGER,
  warning: kinds.WARNING,
  notice: kinds.INFO,
};

function Health() {
  const { data, isFetched, isLoading } = useHealth();

  return (
    <FieldSet legend={translate('Health')}>
      {isLoading ? <LoadingIndicator /> : null}

      {isFetched && !data.length ? (
        <div className={styles.healthOk}>
          {translate('NoIssuesWithYourConfiguration')}
        </div>
      ) : null}

      {data.length ? (
        <>
          <Table columns={columns}>
            <TableBody>
              {data.map((item) => (
                <TableRow key={`health${item.source}`}>
                  <TableRowCell>
                    <Icon
                      name={icons.DANGER}
                      kind={KINDS[item.type] ?? kinds.WARNING}
                      title={titleCase(item.type)}
                    />
                  </TableRowCell>
                  <TableRowCell>{item.message}</TableRowCell>
                  <TableRowCell>
                    {SETTINGS_LINKS[item.source] ? (
                      <IconButton
                        name={icons.SETTINGS}
                        to={SETTINGS_LINKS[item.source]}
                        title={translate('Settings')}
                      />
                    ) : null}
                  </TableRowCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>

          <Alert kind={kinds.INFO}>
            <InlineMarkdown
              data={translate('HealthMessagesInfoBox', {
                link: '/system/logs/files',
              })}
            />
          </Alert>
        </>
      ) : null}
    </FieldSet>
  );
}

export default Health;
