import React, { useCallback, useState } from 'react';
import { useTrackedCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import ToolbarItem from 'Components/Page/Toolbar/ToolbarItem';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { icons, kinds } from 'Helpers/Props';
import EditSourceModal from 'Sources/Edit/EditSourceModal';
import { useInvalidateSources, useSources } from 'Sources/useSources';
import translate from 'Utilities/String/translate';
import SourceRow from './SourceRow';

const columns: Column[] = [
  { name: 'name', label: () => translate('Name'), isVisible: true },
  { name: 'type', label: () => translate('Type'), isVisible: true },
  { name: 'eventCount', label: () => translate('Events'), isVisible: true },
  {
    name: 'lastRefresh',
    label: () => translate('LastRefresh'),
    isVisible: true,
  },
  { name: 'lastResult', label: () => translate('LastResult'), isVisible: true },
  {
    name: 'nextRefresh',
    label: () => translate('NextRefresh'),
    isVisible: true,
  },
  { name: 'enabled', label: () => translate('Enabled'), isVisible: true },
  { name: 'actions', label: '', isVisible: true },
];

function Sources() {
  const { data: sources, isLoading, error } = useSources();
  const [isAddOpen, setIsAddOpen] = useState(false);

  const invalidateSources = useInvalidateSources();
  const { execute: refreshAll, isExecuting: isRefreshingAll } =
    useTrackedCommand('/command', invalidateSources);

  const handleRefreshAllPress = useCallback(() => {
    refreshAll({ name: 'RefreshSources', force: true });
  }, [refreshAll]);

  return (
    <PageContent title={translate('Sources')}>
      <PageToolbar>
        <ToolbarItem
          id="add"
          priority={1}
          groupId="left"
          label={translate('AddSource')}
          iconName={icons.ADD}
          onPress={() => setIsAddOpen(true)}
        />

        <ToolbarItem
          id="refresh-all"
          priority={1}
          groupId="left"
          label={translate('RefreshAll')}
          iconName={icons.REFRESH}
          isSpinning={isRefreshingAll}
          isDisabled={!sources.length}
          onPress={handleRefreshAllPress}
        />
      </PageToolbar>

      <PageContentBody>
        <PageHeading title={translate('Sources')} />

        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{translate('SourcesLoadError')}</Alert>
        ) : null}

        {!isLoading && !error && !sources.length ? (
          <Alert kind={kinds.INFO}>{translate('NoSources')}</Alert>
        ) : null}

        {sources.length ? (
          <Table columns={columns}>
            <TableBody>
              {sources.map((source) => (
                <SourceRow key={source.id} source={source} />
              ))}
            </TableBody>
          </Table>
        ) : null}
      </PageContentBody>

      <EditSourceModal
        isOpen={isAddOpen}
        onModalClose={() => setIsAddOpen(false)}
      />
    </PageContent>
  );
}

export default Sources;
