// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Calendar/CalendarPage.tsx
// No RSS sync, missing search, options modal or custom filters yet.
import React, { useCallback, useState } from 'react';
import FilterMenu from 'Components/Menu/FilterMenu';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarSpacer from 'Components/Page/Toolbar/PageToolbarSpacer';
import ToolbarItem from 'Components/Page/Toolbar/ToolbarItem';
import { align, icons } from 'Helpers/Props';
import NoSeries from 'Series/NoSeries';
import useSeries from 'Series/useSeries';
import translate from 'Utilities/String/translate';
import Calendar from './Calendar';
import { setCalendarOption, useCalendarOption } from './calendarOptionsStore';
import CalendarLinkModal from './iCal/CalendarLinkModal';
import Legend from './Legend/Legend';
import { FILTERS, useCalendarPage } from './useCalendar';
import styles from './CalendarPage.module.css';

const NO_CUSTOM_FILTERS: never[] = [];

function CalendarPage() {
  const selectedFilterKey = useCalendarOption('selectedFilterKey');
  const { data: allSeries, isFetched } = useSeries();
  const hasSeries = allSeries.length > 0;

  useCalendarPage();

  const [isCalendarLinkModalOpen, setIsCalendarLinkModalOpen] = useState(false);

  const handleGetCalendarLinkPress = useCallback(() => {
    setIsCalendarLinkModalOpen(true);
  }, []);

  const handleGetCalendarLinkModalClose = useCallback(() => {
    setIsCalendarLinkModalOpen(false);
  }, []);

  const handleFilterSelect = useCallback((key: string | number) => {
    setCalendarOption('selectedFilterKey', key);
  }, []);

  return (
    <PageContent title={translate('Calendar')}>
      <PageToolbar>
        <ToolbarItem
          id="ical-link"
          priority={1}
          groupId="left-a"
          label={translate('ICalLink')}
          iconName={icons.CALENDAR}
          onPress={handleGetCalendarLinkPress}
        />

        <PageToolbarSpacer />

        <ToolbarItem id="filter" pinned={true}>
          <FilterMenu
            alignMenu={align.RIGHT}
            isDisabled={!hasSeries}
            selectedFilterKey={selectedFilterKey}
            filters={FILTERS}
            customFilters={NO_CUSTOM_FILTERS}
            onFilterSelect={handleFilterSelect}
          />
        </ToolbarItem>
      </PageToolbar>

      <PageContentBody
        className={styles.calendarPageBody}
        innerClassName={styles.calendarInnerPageBody}
      >
        {hasSeries ? <Calendar /> : null}
        {isFetched && !hasSeries ? <NoSeries /> : null}
        {hasSeries ? <Legend /> : null}
      </PageContentBody>

      <CalendarLinkModal
        isOpen={isCalendarLinkModalOpen}
        onModalClose={handleGetCalendarLinkModalClose}
      />
    </PageContent>
  );
}

export default CalendarPage;
