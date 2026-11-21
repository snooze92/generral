// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Calendar/useCalendar.ts
// Only the built-in filters (no custom filters, no specials filter) and no episode query key sharing.
import { keepPreviousData } from '@tanstack/react-query';
import moment, { Moment, MomentInput } from 'moment-timezone';
import { useEffect, useMemo } from 'react';
import { create } from 'zustand';
import { Filter } from 'Filters/Filter';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import { CalendarItem } from 'typings/Calendar';
import translate from 'Utilities/String/translate';
import { getCalendarOption, useCalendarOption } from './calendarOptionsStore';
import { CalendarView } from './calendarViews';

export const FILTERS: Filter[] = [
  {
    key: 'all',
    label: () => translate('All'),
    filters: [],
  },
  {
    key: 'monitored',
    label: () => translate('MonitoredOnly'),
    filters: [
      {
        key: 'unmonitored',
        value: [false],
        type: 'equal',
      },
    ],
  },
];

const NO_ITEMS: CalendarItem[] = [];

interface CalendarStore {
  time: Moment;
  dates: string[];
  dayCount: number;
}

const calendarStore = create<CalendarStore>(() => ({
  time: moment(),
  dates: [],
  dayCount: 7,
}));

const VIEW_RANGES: Record<
  CalendarView,
  moment.unitOfTime.DurationConstructor | undefined
> = {
  agenda: undefined,
  day: 'day',
  week: 'week',
  month: 'month',
  forecast: 'day',
};

const useCalendar = () => {
  const dates = useCalendarDates();
  const time = useCalendarTime();
  const selectedFilterKey = useCalendarOption('selectedFilterKey');
  const view = useCalendarOption('view');

  const { start, end } = useMemo(() => {
    return getPopulatableRange(dates[0], dates[dates.length - 1], view);
  }, [dates, view]);

  const unmonitored = selectedFilterKey === 'all';

  const result = useApiQuery<CalendarItem[]>({
    path: '/calendar',
    queryParams: {
      start,
      end,
      unmonitored,
    },
    queryOptions: {
      enabled: !!time && !!start && !!end,
      placeholderData: keepPreviousData,
    },
  });

  return {
    ...result,
    data: result.data ?? NO_ITEMS,
  };
};

export default useCalendar;

export const useCalendarPage = () => {
  const dayCount = useCalendarDayCount();
  const time = useCalendarTime();
  const view = useCalendarOption('view');
  const { firstDayOfWeek, timeZone } = useUiSettingsValues();

  useEffect(() => {
    calendarStore.setState({
      time: timeZone ? moment.tz(timeZone) : moment(),
    });
  }, [timeZone]);

  useEffect(() => {
    const { dates } = getDates(time, view, firstDayOfWeek, dayCount);

    calendarStore.setState({ dates });
  }, [firstDayOfWeek, dayCount, time, view]);
};

export const useCalendarTime = () => {
  return calendarStore((state) => state.time);
};

export const useCalendarDates = () => {
  return calendarStore((state) => state.dates);
};

export const useCalendarDayCount = () => {
  return calendarStore((state) => state.dayCount);
};

export const useCalendarRange = () => {
  const dates = useCalendarDates();

  return {
    start: dates[0],
    end: dates[dates.length - 1],
  };
};

export const setCalendarDayCount = (dayCount: number) => {
  calendarStore.setState({ dayCount });
};

export const goToToday = () => {
  setCalendarTime(moment());
};

export const goToDate = (date: MomentInput) => {
  const view = getCalendarOption('view');
  const selected = moment(date).startOf('day');

  if (view === 'forecast') {
    const { time } = calendarStore.getState();
    const currentStartWeekday = moment(time).subtract(1, 'day').day();
    const diff = (selected.day() - currentStartWeekday + 7) % 7;

    setCalendarTime(selected.subtract(diff, 'days').add(1, 'day'));
    return;
  }

  setCalendarTime(selected);
};

export const goToPreviousRange = () => {
  const { dayCount, time } = calendarStore.getState();
  const view = getCalendarOption('view');

  const amount = view === 'forecast' ? dayCount : 1;
  const newTime = moment(time).subtract(amount, VIEW_RANGES[view]);

  setCalendarTime(newTime);
};

export const goToNextRange = () => {
  const { dayCount, time } = calendarStore.getState();
  const view = getCalendarOption('view');

  const amount = view === 'forecast' ? dayCount : 1;
  const newTime = moment(time).add(amount, VIEW_RANGES[view]);

  setCalendarTime(newTime);
};

const setCalendarTime = (time: Moment) => {
  calendarStore.setState({ time });
};

const getDays = (start: Moment, end: Moment) => {
  const startTime = moment(start);
  const endTime = moment(end);
  const difference = endTime.diff(startTime, 'days');

  return Array(difference + 1)
    .fill(0)
    .map((_, i) => startTime.clone().add(i, 'days').toISOString());
};

const getDates = (
  time: Moment,
  view: CalendarView,
  firstDayOfWeek: number,
  dayCount: number
) => {
  const weekName = firstDayOfWeek === 0 ? 'week' : 'isoWeek';

  const weekTime = time.clone().locale('en');

  let start = time.clone().startOf('day');
  let end = time.clone().endOf('day');

  if (view === 'week') {
    start = weekTime.clone().startOf(weekName);
    end = weekTime.clone().endOf(weekName);
  }

  if (view === 'forecast') {
    start = time.clone().subtract(1, 'day').startOf('day');
    end = time
      .clone()
      .add(dayCount - 2, 'days')
      .endOf('day');
  }

  if (view === 'month') {
    start = weekTime.clone().startOf('month').startOf(weekName);
    end = weekTime.clone().endOf('month').endOf(weekName);
  }

  if (view === 'agenda') {
    start = time.clone().subtract(1, 'day').startOf('day');
    end = time.clone().add(1, 'month').endOf('day');
  }

  return {
    start: start.toISOString(),
    end: end.toISOString(),
    time: time.toISOString(),
    dates: getDays(start, end),
  };
};

function getPopulatableRange(
  startDate: string,
  endDate: string,
  view: CalendarView
) {
  switch (view) {
    case 'day':
      return {
        start: moment(startDate).subtract(1, 'day').toISOString(),
        end: moment(endDate).add(1, 'day').toISOString(),
      };
    case 'week':
    case 'forecast':
      return {
        start: moment(startDate).subtract(1, 'week').toISOString(),
        end: moment(endDate).add(1, 'week').toISOString(),
      };
    default:
      return {
        start: startDate,
        end: endDate,
      };
  }
}
