// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Calendar/Events/CalendarEvent.tsx
// Links to the series page (no episode details modal, queue or files yet).
import classNames from 'classnames';
import moment from 'moment-timezone';
import React from 'react';
import { useCalendarOptions } from 'Calendar/calendarOptionsStore';
import getStatusStyle from 'Calendar/getStatusStyle';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons, kinds } from 'Helpers/Props';
import { useSingleSeries } from 'Series/useSeries';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import { CalendarItem } from 'typings/Calendar';
import formatDateTime from 'Utilities/Date/formatDateTime';
import formatTime from 'Utilities/Date/formatTime';
import translate from 'Utilities/String/translate';
import getCalendarStatusLabel from './getCalendarStatusLabel';
import getEpisodeLabel from './getEpisodeLabel';
import styles from './CalendarEvent.module.css';

function CalendarEvent(props: CalendarItem) {
  const {
    seriesId,
    title = '',
    seasonNumber,
    episodeNumber,
    airDateUtc,
    runtime,
    monitored,
    hasFile,
  } = props;

  const series = useSingleSeries(seriesId);
  const { longDateFormat, timeFormat } = useUiSettingsValues();
  const { fullColorEvents } = useCalendarOptions();

  if (!series) {
    return null;
  }

  const startTime = moment(airDateUtc);
  const endTime = moment(airDateUtc).add(runtime, 'minutes');
  const isMonitored = series.monitored && monitored;
  const statusStyle = getStatusStyle(
    hasFile,
    false,
    startTime,
    endTime,
    isMonitored
  );
  const episode = getEpisodeLabel(series.seriesType, seasonNumber, episodeNumber);

  const eventLabel = translate('CalendarOpenEpisodeDetails', {
    series: series.title,
    episode,
    title,
    airDate: formatDateTime(airDateUtc, longDateFormat, timeFormat),
    status: getCalendarStatusLabel(statusStyle),
  });

  return (
    <div
      className={classNames(
        styles.event,
        styles[statusStyle],
        fullColorEvents && 'fullColor'
      )}
    >
      <Link
        className={styles.underlay}
        to={`/series/${series.titleSlug}`}
        aria-label={eventLabel}
      />

      <div className={styles.overlay}>
        <div className={styles.info}>
          <div className={styles.seriesTitle}>{series.title}</div>

          <div
            className={classNames(
              styles.statusContainer,
              fullColorEvents && 'fullColor'
            )}
          >
            {episodeNumber === 1 && seasonNumber > 0 ? (
              <Icon
                className={styles.statusIcon}
                name={icons.PREMIERE}
                kind={kinds.INFO}
                title={translate('SeasonPremiere')}
              />
            ) : null}
          </div>
        </div>

        <div className={styles.episodeInfo}>
          <div className={styles.episodeTitle}>{title}</div>
          {episode ? <div>{episode}</div> : null}
        </div>

        <div className={styles.airTime}>
          {formatTime(airDateUtc, timeFormat)} -{' '}
          {formatTime(endTime.toISOString(), timeFormat, {
            includeMinuteZero: true,
          })}
        </div>
      </div>
    </div>
  );
}

export default CalendarEvent;
