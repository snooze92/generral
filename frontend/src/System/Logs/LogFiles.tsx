// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/System/Logs/LogFiles.tsx and LogFilesTableRow.tsx
import React from 'react';
import Alert from 'Components/Alert';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import InlineMarkdown from 'Components/Markdown/InlineMarkdown';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import ToolbarItem from 'Components/Page/Toolbar/ToolbarItem';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { icons, kinds } from 'Helpers/Props';
import { useSystemStatusData } from 'System/Status/useSystemStatus';
import translate from 'Utilities/String/translate';
import { useLogFiles, withApiKey } from '../useSystemData';
import styles from './LogFiles.module.css';

const columns: Column[] = [
  { name: 'filename', label: () => translate('Filename'), isVisible: true },
  {
    name: 'lastWriteTime',
    label: () => translate('LastWriteTime'),
    isVisible: true,
  },
  { name: 'download', label: '', isVisible: true },
];

function LogFiles() {
  const { data, isFetching, isFetched, refetch } = useLogFiles();
  const { appData } = useSystemStatusData();

  return (
    <PageContent title={translate('LogFiles')}>
      <PageToolbar>
        <ToolbarItem
          id="refresh"
          priority={1}
          groupId="left"
          label={translate('Refresh')}
          iconName={icons.REFRESH}
          isSpinning={isFetching}
          onPress={() => refetch()}
        />
      </PageToolbar>

      <PageContentBody>
        <PageHeading
          scope={translate('System')}
          title={translate('LogFiles')}
        />

        <Alert>
          <InlineMarkdown
            data={translate('LogFilesLocation', {
              location: `${appData}/logs`,
            })}
          />
        </Alert>

        {isFetching && !isFetched ? <LoadingIndicator /> : null}

        {isFetched && !data.length ? (
          <Alert kind={kinds.INFO}>{translate('NoLogFiles')}</Alert>
        ) : null}

        {data.length ? (
          <Table columns={columns}>
            <TableBody>
              {data.map((file) => (
                <TableRow key={file.filename}>
                  <TableRowCell>{file.filename}</TableRowCell>
                  <RelativeDateCell
                    date={file.lastWriteTime}
                    includeTime={true}
                  />
                  <TableRowCell className={styles.download}>
                    <Link
                      to={withApiKey(file.downloadUrl)}
                      target="_blank"
                      noRouter={true}
                    >
                      {translate('Download')}
                    </Link>
                  </TableRowCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default LogFiles;
