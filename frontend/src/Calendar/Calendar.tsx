// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Calendar/Calendar.tsx
// No SignalR page populator or series refresh command yet.
import React, { useEffect, useLayoutEffect, useRef } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import useCurrentPage from 'Helpers/Hooks/useCurrentPage';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import Agenda from './Agenda/Agenda';
import { useCalendarOption } from './calendarOptionsStore';
import CalendarDays from './Day/CalendarDays';
import DaysOfWeek from './Day/DaysOfWeek';
import CalendarHeader from './Header/CalendarHeader';
import useCalendar, { goToToday } from './useCalendar';
import styles from './Calendar.module.css';

const UPDATE_DELAY = 3600000; // 1 hour

function Calendar() {
  const requestCurrentPage = useCurrentPage();
  const { isFetching, isLoading, error } = useCalendar();
  const view = useCalendarOption('view');
  const contentRef = useRef<HTMLDivElement>(null);
  const stickyRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const interval = setInterval(goToToday, UPDATE_DELAY);

    return () => clearInterval(interval);
  }, []);

  useEffect(() => {
    if (!requestCurrentPage) {
      goToToday();
    }
  }, [requestCurrentPage]);

  useLayoutEffect(() => {
    const content = contentRef.current;
    const sticky = stickyRef.current;

    if (!content || !sticky) {
      return;
    }

    const updateHeight = () =>
      content.style.setProperty(
        '--calendarStickyHeight',
        `${sticky.offsetHeight}px`
      );

    updateHeight();

    const observer = new ResizeObserver(updateHeight);
    observer.observe(sticky);

    return () => observer.disconnect();
  }, [isLoading, error, view]);

  return (
    <div className={styles.calendar}>
      {isLoading ? <LoadingIndicator /> : null}

      {!isFetching && error ? (
        <Alert kind={kinds.DANGER}>{translate('CalendarLoadError')}</Alert>
      ) : null}

      {!error && !isLoading ? (
        <div
          ref={view === 'agenda' ? undefined : contentRef}
          className={styles.calendarContent}
        >
          <div
            ref={view === 'agenda' ? undefined : stickyRef}
            className={styles.sticky}
          >
            <CalendarHeader />
            {view === 'agenda' ? null : <DaysOfWeek />}
          </div>

          {view === 'agenda' ? <Agenda /> : <CalendarDays />}
        </div>
      ) : null}
    </div>
  );
}

export default Calendar;
