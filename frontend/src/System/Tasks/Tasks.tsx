// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/System/Tasks/Tasks.tsx, Scheduled/ScheduledTasks.tsx,
// Scheduled/ScheduledTaskRow.tsx and Queued/QueuedTasks.tsx
import moment from 'moment-timezone';
import React from 'react';
import { useTrackedCommand } from 'Commands/useCommands';
import FieldSet from 'Components/FieldSet';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { icons } from 'Helpers/Props';
import formatTimeSpan from 'Utilities/Date/formatTimeSpan';
import titleCase from 'Utilities/String/titleCase';
import translate from 'Utilities/String/translate';
import {
  Task,
  useInvalidate,
  useQueuedCommands,
  useTasks,
} from '../useSystemData';
import styles from './Tasks.module.css';

const scheduledColumns: Column[] = [
  { name: 'name', label: () => translate('Name'), isVisible: true },
  { name: 'interval', label: () => translate('Interval'), isVisible: true },
  {
    name: 'lastExecution',
    label: () => translate('LastExecution'),
    isVisible: true,
  },
  {
    name: 'lastDuration',
    label: () => translate('LastDuration'),
    isVisible: true,
  },
  {
    name: 'nextExecution',
    label: () => translate('NextExecution'),
    isVisible: true,
  },
  { name: 'actions', label: '', isVisible: true },
];

const queuedColumns: Column[] = [
  { name: 'commandName', label: () => translate('Name'), isVisible: true },
  { name: 'status', label: () => translate('Status'), isVisible: true },
  { name: 'trigger', label: () => translate('Trigger'), isVisible: true },
  { name: 'queued', label: () => translate('Queued'), isVisible: true },
  { name: 'started', label: () => translate('Started'), isVisible: true },
  { name: 'ended', label: () => translate('Ended'), isVisible: true },
  { name: 'message', label: () => translate('Message'), isVisible: true },
];

function ScheduledTaskRow({ task }: { task: Task }) {
  const invalidate = useInvalidate('/system/task');
  const { execute, isExecuting } = useTrackedCommand('/command', invalidate);
  const isDisabled = task.interval === 0;
  const runLabel = translate('RunTaskWithName', { name: task.name });

  return (
    <TableRow>
      <TableRowCell>{task.name}</TableRowCell>
      <TableRowCell className={styles.interval}>
        {isDisabled
          ? translate('Disabled')
          : moment
              .duration(task.interval, 'minutes')
              .humanize()
              .replace(/^an?(?=\s)/, '1')}
      </TableRowCell>
      {task.lastExecution ? (
        <RelativeDateCell
          className={styles.date}
          date={task.lastExecution}
          includeTime={true}
        />
      ) : (
        <TableRowCell className={styles.date}>-</TableRowCell>
      )}
      <TableRowCell className={styles.date}>
        {task.lastDuration ? formatTimeSpan(task.lastDuration) : '-'}
      </TableRowCell>
      {task.nextExecution && !isDisabled ? (
        <RelativeDateCell
          className={styles.date}
          date={task.nextExecution}
          includeTime={true}
        />
      ) : (
        <TableRowCell className={styles.date}>-</TableRowCell>
      )}
      <TableRowCell className={styles.actions}>
        <SpinnerIconButton
          name={icons.REFRESH}
          spinningName={icons.REFRESH}
          title={runLabel}
          aria-label={runLabel}
          isSpinning={isExecuting}
          onPress={() => execute({ name: task.taskName })}
        />
      </TableRowCell>
    </TableRow>
  );
}

function Tasks() {
  const { data: tasks, isLoading } = useTasks();
  const { data: commands } = useQueuedCommands();

  return (
    <PageContent title={translate('Tasks')}>
      <PageContentBody>
        <PageHeading scope={translate('System')} title={translate('Tasks')} />

        <FieldSet legend={translate('Scheduled')}>
          {isLoading ? <LoadingIndicator /> : null}

          <Table columns={scheduledColumns}>
            <TableBody>
              {tasks.map((task) => (
                <ScheduledTaskRow key={task.taskName} task={task} />
              ))}
            </TableBody>
          </Table>
        </FieldSet>

        <FieldSet legend={translate('Queue')}>
          <Table columns={queuedColumns}>
            <TableBody>
              {commands.map((command) => (
                <TableRow key={command.id}>
                  <TableRowCell>{command.name}</TableRowCell>
                  <TableRowCell>{titleCase(command.status)}</TableRowCell>
                  <TableRowCell>{titleCase(command.trigger)}</TableRowCell>
                  <RelativeDateCell date={command.queued} includeTime={true} />
                  <RelativeDateCell date={command.started} includeTime={true} />
                  <RelativeDateCell date={command.ended} includeTime={true} />
                  <TableRowCell>{command.message}</TableRowCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </FieldSet>
      </PageContentBody>
    </PageContent>
  );
}

export default Tasks;
