import React from 'react';
import Label, { LabelProps } from 'Components/Label';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import padNumber from 'Utilities/Number/padNumber';
import translate from 'Utilities/String/translate';
import { MappingChange, MappingPreview, MappingPreviewRow } from './Mapping';
import styles from './MappingPreviewTable.module.css';

const columns: Column[] = [
  { name: 'event', label: () => translate('Event'), isVisible: true },
  { name: 'number', label: () => translate('Episode'), isVisible: true },
  { name: 'title', label: () => translate('EpisodeTitle'), isVisible: true },
  { name: 'airDate', label: () => translate('AirDate'), isVisible: true },
  { name: 'change', label: () => translate('Change'), isVisible: true },
];

const changeKinds: Record<MappingChange, LabelProps['kind']> = {
  new: 'success',
  changed: 'warning',
  unchanged: 'default',
  orphaned: 'danger',
  filtered: 'disabled',
  error: 'danger',
};

const changeLabels: Record<MappingChange, () => string> = {
  new: () => translate('MappingChangeNew'),
  changed: () => translate('MappingChangeChanged'),
  unchanged: () => translate('MappingChangeUnchanged'),
  orphaned: () => translate('Orphaned'),
  filtered: () => translate('MappingChangeFiltered'),
  error: () => translate('Error'),
};

function episodeNumber(row: MappingPreviewRow) {
  if (row.seasonNumber == null || row.episodeNumber == null) {
    return '';
  }

  return `${row.seasonNumber}x${padNumber(row.episodeNumber, 2)}`;
}

function PreviewRow({ row }: { row: MappingPreviewRow }) {
  return (
    <TableRow className={row.kept ? undefined : styles.rejected}>
      <TableRowCell>
        <div className={styles.eventTitle}>{row.event.title}</div>
        <div className={styles.eventKey}>{row.event.key}</div>
      </TableRowCell>
      <TableRowCell className={styles.number}>{episodeNumber(row)}</TableRowCell>
      <TableRowCell>
        {row.title}
        {row.errors.map((error, index) => (
          <div key={index} className={styles.error}>
            {error}
          </div>
        ))}
      </TableRowCell>
      <RelativeDateCell date={row.airDateUtc} includeTime={true} />
      <TableRowCell>
        <Label kind={changeKinds[row.change]}>
          {changeLabels[row.change]?.() ?? row.change}
        </Label>
        {row.rejectedBy ? (
          <div className={styles.rejectedBy}>{row.rejectedBy}</div>
        ) : null}
      </TableRowCell>
    </TableRow>
  );
}

interface MappingPreviewTableProps {
  preview: MappingPreview;
}

/** The preview table and summary bar (docs/specs/05-mapping.md § Preview & dry-run). */
function MappingPreviewTable({ preview }: MappingPreviewTableProps) {
  const { summary } = preview;

  return (
    <section aria-label={translate('Preview')}>
      <div className={styles.summary} role="status">
        {translate('MappingPreviewSummary', {
          events: summary.events,
          kept: summary.kept,
          episodes: summary.episodes,
          seasons: preview.seasons.length,
        })}
        {summary.errors ? (
          <span className={styles.summaryErrors}>
            {translate('MappingPreviewErrors', { count: summary.errors })}
          </span>
        ) : null}
      </div>

      <Table columns={columns}>
        <TableBody>
          {preview.rows.map((row) => (
            <PreviewRow key={`${row.event.id}:${row.partKey ?? ''}`} row={row} />
          ))}
        </TableBody>
      </Table>
    </section>
  );
}

export default MappingPreviewTable;
