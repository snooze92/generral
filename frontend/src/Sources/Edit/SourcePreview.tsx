import classNames from 'classnames';
import React, { useState } from 'react';
import Alert from 'Components/Alert';
import Link from 'Components/Link/Link';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { kinds } from 'Helpers/Props';
import eventColumns from 'Sources/Events/eventColumns';
import EventsTableRow from 'Sources/Events/EventsTableRow';
import { SourceTestResult } from 'Sources/Source';
import translate from 'Utilities/String/translate';
import styles from './SourcePreview.module.css';

type Tab = 'events' | 'itemErrors' | 'raw';

interface SourcePreviewProps {
  result: SourceTestResult;
}

function SourcePreview({ result }: SourcePreviewProps) {
  const [tab, setTab] = useState<Tab>('events');

  const tabs: { id: Tab; label: string }[] = [
    {
      id: 'events',
      label: `${translate('Events')} (${result.events.length})`,
    },
    {
      id: 'itemErrors',
      label: `${translate('ItemErrors')} (${result.itemErrors.length})`,
    },
    {
      id: 'raw',
      label: `${translate('RawResponses')} (${result.responses.length})`,
    },
  ];

  return (
    <section className={styles.preview} aria-label={translate('Preview')}>
      {result.success ? null : (
        <Alert kind={kinds.DANGER}>
          {translate('SourceTestFailed', { error: result.error ?? '' })}
        </Alert>
      )}

      {result.warnings.map((warning, index) => (
        <Alert key={index} kind={kinds.WARNING}>
          {warning}
        </Alert>
      ))}

      <div className={styles.tabs} role="tablist">
        {tabs.map(({ id, label }) => (
          <Link
            key={id}
            className={classNames(styles.tab, tab === id && styles.selectedTab)}
            role="tab"
            aria-selected={tab === id}
            onPress={() => setTab(id)}
          >
            {label}
          </Link>
        ))}
      </div>

      <div role="tabpanel">
        {tab === 'events' ? (
          result.events.length ? (
            <Table columns={eventColumns}>
              <TableBody>
                {result.events.map((event) => (
                  <EventsTableRow key={event.key} event={event} />
                ))}
              </TableBody>
            </Table>
          ) : (
            <Alert kind={kinds.INFO}>{translate('NoEventsExtracted')}</Alert>
          )
        ) : null}

        {tab === 'itemErrors' ? (
          result.itemErrors.length ? (
            <ul className={styles.itemErrors}>
              {result.itemErrors.map((itemError) => (
                <li key={`${itemError.url}-${itemError.index}`}>
                  <div className={styles.itemErrorTitle}>
                    {translate('ItemNumber', { index: itemError.index })}
                    {itemError.url ? ` · ${itemError.url}` : ''}
                  </div>
                  {itemError.errors.map((error, index) => (
                    <div key={index} className={styles.error}>
                      {error}
                    </div>
                  ))}
                  <pre className={styles.body}>
                    {JSON.stringify(itemError.fields, null, 2)}
                  </pre>
                </li>
              ))}
            </ul>
          ) : (
            <Alert kind={kinds.SUCCESS}>{translate('NoItemErrors')}</Alert>
          )
        ) : null}

        {tab === 'raw'
          ? result.responses.map((response, index) => (
              <div key={index} className={styles.response}>
                <div className={styles.responseSummary}>
                  <span>{response.status}</span>
                  <span>{response.contentType}</span>
                  <span className={styles.url}>{response.url}</span>
                  {response.fromCache ? (
                    <span>{translate('Cached')}</span>
                  ) : null}
                </div>
                <pre className={styles.body}>
                  {response.body}
                  {response.truncated ? `\n${translate('Truncated')}` : ''}
                </pre>
              </div>
            ))
          : null}
      </div>
    </section>
  );
}

export default SourcePreview;
