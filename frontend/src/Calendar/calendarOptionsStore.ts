// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Calendar/calendarOptionsStore.ts
// M1 has only the month and agenda views and no options modal, so the icon options stay off.
import { SelectedFilterKey } from 'Components/Filter/Filter';
import { createOptionsStore } from 'Helpers/Hooks/useOptionsStore';
import { CalendarView } from './calendarViews';

export interface CalendarOptions {
  showFinaleIcon: boolean;
  showSpecialIcon: boolean;
  showCutoffUnmetIcon: boolean;
  fullColorEvents: boolean;
  selectedFilterKey: SelectedFilterKey;
  view: CalendarView;
}

const { useOptions, useOption, getOptions, getOption, setOptions, setOption } =
  createOptionsStore<CalendarOptions>('calendar_options', () => {
    return {
      showFinaleIcon: false,
      showSpecialIcon: false,
      showCutoffUnmetIcon: false,
      fullColorEvents: false,
      selectedFilterKey: 'monitored',
      view: 'month',
    };
  });

export const useCalendarOptions = useOptions;
export const getCalendarOptions = getOptions;
export const setCalendarOptions = setOptions;
export const useCalendarOption = useOption;
export const getCalendarOption = getOption;
export const setCalendarOption = setOption;
