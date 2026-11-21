// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Calendar/Header/CalendarHeader.tsx
// M1 has only the month and agenda views.
import moment from 'moment-timezone';
import React, { useCallback, useMemo } from 'react';
import { useAppDimensions } from 'App/appStore';
import {
  setCalendarOption,
  useCalendarOption,
} from 'Calendar/calendarOptionsStore';
import { CalendarView } from 'Calendar/calendarViews';
import useCalendar, {
  goToDate,
  goToNextRange,
  goToPreviousRange,
  goToToday,
  useCalendarRange,
  useCalendarTime,
} from 'Calendar/useCalendar';
import DateInput from 'Components/DateInput';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import CalendarHeaderViewButton from './CalendarHeaderViewButton';
import styles from './CalendarHeader.module.css';

function getNavigationLabels(view: CalendarView) {
  if (view === 'month') {
    return {
      previous: translate('CalendarGoToPreviousMonth'),
      next: translate('CalendarGoToNextMonth'),
    };
  }

  return {
    previous: translate('CalendarGoToPreviousRange'),
    next: translate('CalendarGoToNextRange'),
  };
}

function CalendarHeader() {
  const { isFetching } = useCalendar();
  const view = useCalendarOption('view');
  const time = useCalendarTime();
  const { start } = useCalendarRange();

  const { isSmallScreen } = useAppDimensions();

  const navigationLabels = getNavigationLabels(view);

  const handleViewChange = useCallback((newView: string) => {
    setCalendarOption('view', newView as CalendarView);
  }, []);

  const handleTodayPress = useCallback(() => {
    goToToday();
  }, []);

  const handlePreviousPress = useCallback(() => {
    goToPreviousRange();
  }, []);

  const handleNextPress = useCallback(() => {
    goToNextRange();
  }, []);

  const datePickerValue = useMemo(() => {
    if (view === 'month') {
      return moment(time).locale('en').startOf('month').format('YYYY-MM-DD');
    }

    if (start) {
      return moment(start).locale('en').format('YYYY-MM-DD');
    }

    return '';
  }, [view, time, start]);

  const title = useMemo(() => {
    return view === 'month'
      ? moment(time).format('MMMM YYYY')
      : translate('Agenda');
  }, [time, view]);

  return (
    <div>
      {isSmallScreen ? (
        <div
          className={styles.titleMobile}
          aria-live="polite"
          aria-atomic="true"
        >
          {title}
        </div>
      ) : null}

      <div className={styles.header}>
        <div className={styles.navigationButtons}>
          <Link
            className={styles.navButton}
            isDisabled={view === 'agenda'}
            aria-label={navigationLabels.previous}
            onPress={handlePreviousPress}
          >
            <Icon name={icons.PAGE_PREVIOUS} />
          </Link>

          <Link
            className={styles.navButton}
            isDisabled={view === 'agenda'}
            aria-label={navigationLabels.next}
            onPress={handleNextPress}
          >
            <Icon name={icons.PAGE_NEXT} />
          </Link>

          <Link
            className={styles.todayButton}
            isDisabled={view === 'agenda'}
            onPress={handleTodayPress}
          >
            {translate('Today')}
          </Link>

          <DateInput
            value={datePickerValue}
            label={translate('GoToDate')}
            isDisabled={view === 'agenda'}
            onChange={goToDate}
          />
        </div>

        {isSmallScreen ? null : (
          <div
            className={styles.titleDesktop}
            aria-live="polite"
            aria-atomic="true"
          >
            {title}
          </div>
        )}

        <div className={styles.viewButtonsContainer}>
          {isFetching ? (
            <LoadingIndicator className={styles.loading} size={20} />
          ) : null}

          <CalendarHeaderViewButton
            className={styles.viewButton}
            view="month"
            selectedView={view}
            onPress={handleViewChange}
          />

          <CalendarHeaderViewButton
            className={styles.viewButton}
            view="agenda"
            selectedView={view}
            onPress={handleViewChange}
          />
        </div>
      </div>
    </div>
  );
}

export default CalendarHeader;
