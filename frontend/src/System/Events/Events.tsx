// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/System/Events/LogsTable.tsx, LogsTableRow.tsx and LogsTableDetailsModal.tsx
import React, { useState } from 'react';
import Alert from 'Components/Alert';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
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
import TablePager from 'Components/Table/TablePager';
import TableRowButton from 'Components/Table/TableRowButton';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { LogEvent, useEvents } from '../useSystemData';
import styles from './Events.module.css';

const columns: Column[] = [
  { name: 'level', label: '', isVisible: true },
  { name: 'time', label: () => translate('Time'), isVisible: true },
  { name: 'logger', label: () => translate('Component'), isVisible: true },
  { name: 'message', label: () => translate('Message'), isVisible: true },
];

const LEVEL_ICONS = {
  info: icons.INFO,
  warn: icons.WARNING,
  error: icons.DANGER,
  fatal: icons.FATAL,
};

const FILTERS = [
  { key: 'all', label: () => translate('All') },
  { key: 'warn', label: () => translate('Warn') },
  { key: 'error', label: () => translate('Error') },
];

function Events() {
  const [page, setPage] = useState(1);
  const [level, setLevel] = useState('all');
  const [details, setDetails] = useState<LogEvent | null>(null);
  const { data, isLoading, isFetching, error, refetch } = useEvents(
    page,
    level
  );
  const records = data?.records ?? [];
  const totalRecords = data?.totalRecords ?? 0;

  return (
    <PageContent title={translate('Events')}>
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
        {FILTERS.map((filter) => (
          <ToolbarItem
            key={filter.key}
            id={`filter-${filter.key}`}
            priority={1}
            groupId="right"
            label={filter.label()}
            iconName={icons.FILTER}
            isDisabled={level === filter.key}
            onPress={() => {
              setLevel(filter.key);
              setPage(1);
            }}
          />
        ))}
      </PageToolbar>

      <PageContentBody>
        <PageHeading scope={translate('System')} title={translate('Events')} />

        <Alert>{translate('EventsInfo')}</Alert>

        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{translate('EventsLoadError')}</Alert>
        ) : null}

        {!isLoading && !error && !records.length ? (
          <Alert kind={kinds.INFO}>{translate('NoEventsFound')}</Alert>
        ) : null}

        {records.length ? (
          <>
            <Table columns={columns}>
              <TableBody>
                {records.map((event) => (
                  <TableRowButton
                    key={event.id}
                    onPress={() => setDetails(event)}
                  >
                    <TableRowCell className={styles.level}>
                      <Icon
                        className={styles[event.level]}
                        name={LEVEL_ICONS[event.level]}
                        title={event.level}
                      />
                    </TableRowCell>
                    <RelativeDateCell date={event.time} includeTime={true} />
                    <TableRowCell>{event.logger}</TableRowCell>
                    <TableRowCell>{event.message}</TableRowCell>
                  </TableRowButton>
                ))}
              </TableBody>
            </Table>

            <TablePager
              page={page}
              totalPages={Math.max(1, Math.ceil(totalRecords / 50))}
              totalRecords={totalRecords}
              isFetching={isFetching}
              onPageSelect={setPage}
            />
          </>
        ) : null}

        <Modal isOpen={!!details} onModalClose={() => setDetails(null)}>
          <ModalContent onModalClose={() => setDetails(null)}>
            <ModalHeader>{translate('Details')}</ModalHeader>
            <ModalBody>
              <div>{translate('Message')}</div>
              <pre className={styles.detailsText}>{details?.message}</pre>
              {details?.exception ? (
                <>
                  <div>{translate('Exception')}</div>
                  <pre className={styles.detailsText}>{details.exception}</pre>
                </>
              ) : null}
            </ModalBody>
            <ModalFooter>
              <Button onPress={() => setDetails(null)}>
                {translate('Close')}
              </Button>
            </ModalFooter>
          </ModalContent>
        </Modal>
      </PageContentBody>
    </PageContent>
  );
}

export default Events;
