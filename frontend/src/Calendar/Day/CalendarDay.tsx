// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Calendar/Day/CalendarDay.tsx
// No collapsed episode groups and no day view yet.
import classNames from 'classnames';
import moment from 'moment-timezone';
import React, { useMemo } from 'react';
import { useCalendarOption } from 'Calendar/calendarOptionsStore';
import * as calendarViews from 'Calendar/calendarViews';
import CalendarEvent from 'Calendar/Events/CalendarEvent';
import useCalendar, { useCalendarTime } from 'Calendar/useCalendar';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import styles from './CalendarDay.module.css';

interface CalendarDayProps {
  date: string;
  isTodaysDate: boolean;
  onEventModalOpenToggle(isOpen: boolean): unknown;
}

function CalendarDay({ date, isTodaysDate }: CalendarDayProps) {
  const view = useCalendarOption('view');
  const time = useCalendarTime();
  const { data } = useCalendar();
  const { longDateFormat } = useUiSettingsValues();
  const momentDate = moment(date);

  const events = useMemo(() => {
    const day = moment(date);

    return data.filter((item) => day.isSame(moment(item.airDateUtc), 'day'));
  }, [data, date]);

  const ref = React.useRef<HTMLTableCellElement>(null);

  React.useEffect(() => {
    if (isTodaysDate && view === calendarViews.MONTH && ref.current) {
      ref.current.scrollIntoView?.();
    }
  }, [time, isTodaysDate, view]);

  return (
    <td
      ref={ref}
      className={classNames(styles.day, isTodaysDate && styles.isTodayCell)}
    >
      <div
        className={classNames(
          styles.dayOfMonth,
          isTodaysDate && styles.isToday,
          !momentDate.isSame(moment(time), 'month') && styles.isDifferentMonth
        )}
      >
        <time
          dateTime={momentDate.format('YYYY-MM-DD')}
          aria-current={isTodaysDate ? 'date' : undefined}
        >
          <span className={styles.fullDate}>
            {momentDate.format(longDateFormat)}
          </span>
          <span aria-hidden={true}>{momentDate.date()}</span>
        </time>
      </div>

      <div>
        {events.map((event) => (
          <CalendarEvent key={event.id} {...event} />
        ))}
      </div>
    </td>
  );
}

export default CalendarDay;
