import React from 'react';
import Label, { LabelProps } from 'Components/Label';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import { EventStatus, SourceEvent } from 'Sources/Source';
import translate from 'Utilities/String/translate';

const statusKinds: Record<EventStatus, LabelProps['kind']> = {
  scheduled: 'info',
  postponed: 'warning',
  cancelled: 'danger',
  completed: 'success',
  unknown: 'default',
  gone: 'disabled',
};

const statusLabels: Record<EventStatus, () => string> = {
  scheduled: () => translate('EventStatusScheduled'),
  postponed: () => translate('EventStatusPostponed'),
  cancelled: () => translate('EventStatusCancelled'),
  completed: () => translate('EventStatusCompleted'),
  unknown: () => translate('EventStatusUnknown'),
  gone: () => translate('EventStatusGone'),
};

interface EventsTableRowProps {
  event: SourceEvent;
}

function EventsTableRow({ event }: EventsTableRowProps) {
  return (
    <TableRow>
      <TableRowCell>{event.key}</TableRowCell>
      <TableRowCell>{event.title}</TableRowCell>
      <RelativeDateCell date={event.start} includeTime={true} />
      <TableRowCell>
        <Label kind={statusKinds[event.status]}>
          {statusLabels[event.status]?.() ?? event.status}
        </Label>
      </TableRowCell>
    </TableRow>
  );
}

export default EventsTableRow;
